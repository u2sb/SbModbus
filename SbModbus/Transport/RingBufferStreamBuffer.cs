using System;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Buffers.RingBuffers;
using SbModbus.Utils;

namespace SbModbus.Transport;

/// <summary>
///   <see cref="IStreamBuffer" /> 的默认实现 — 基于 <see cref="FixedSizeRingBuffer{T}" /> + <see cref="SemaphoreSlim" />。
///   提供线程安全的生产者-消费者模式。
/// </summary>
/// <remarks>
///   <para>容量 2048 字节，由 Modbus 最大帧长 256 决定，足够缓冲多条帧。</para>
///   <para>溢出策略：环形缓冲区满时，丢弃最旧数据并记录警告。</para>
///   <para>锁模式：所有缓冲区操作由 lock 语句保护，调用方负责持锁。</para>
/// </remarks>
internal sealed class RingBufferStreamBuffer : IStreamBuffer
{
  private readonly object _locker = new();
  private readonly FixedSizeRingBuffer<byte> _ringBuffer;
  private readonly SemaphoreSlim _dataAvailable = new(0);
  private readonly CancellationTokenSource _disposeCts = new();
  private volatile bool _dataAvailableSignaled;
  private int _isDisposed;

  /// <summary>
  ///   创建缓冲区实例。
  /// </summary>
  /// <param name="capacity">环形缓冲区容量，默认 2048 字节</param>
  public RingBufferStreamBuffer(int capacity = 2048)
  {
    _ringBuffer = new FixedSizeRingBuffer<byte>(capacity);
  }

  /// <inheritdoc />
  public int Count
  {
    get
    {
      lock (_locker) return _ringBuffer.Count;
    }
  }

  /// <inheritdoc />
  public bool IsDisposed => _isDisposed != 0;

  /// <inheritdoc />
  public void Dispose()
  {
    if (Interlocked.CompareExchange(ref _isDisposed, 1, 0) != 0) return;
    _disposeCts.Cancel();
    _dataAvailable.Dispose();
    _disposeCts.Dispose();
  }

  // ---- 以下方法调用方必须在 lock (_locker) 外部调用，由本方法内部持锁 ----

  /// <inheritdoc />
  public void Write(ReadOnlySpan<byte> data)
  {
    lock (_locker)
    {
      if (_isDisposed != 0)
      {
        Logger.Warning("StreamBuffer: Attempted to write after dispose, ignoring");
        return;
      }

      var capacity = _ringBuffer.Capacity;
      var currentCount = _ringBuffer.Count;
      var incoming = data.Length;

      if (currentCount + incoming > capacity)
      {
        var overflow = currentCount + incoming - capacity;
        if (overflow < currentCount)
        {
          _ringBuffer.RemoveFirst(overflow);
          Logger.Warning($"StreamBuffer overflow: discarded {overflow} oldest bytes");
        }
        else
        {
          var keepFromIncoming = capacity;
          _ringBuffer.Clear();
          if (keepFromIncoming < incoming)
            data = data.Slice(incoming - keepFromIncoming);
        }
      }

      _ringBuffer.AddLastRange(data);

      if (!_dataAvailableSignaled)
      {
        _dataAvailable.Release();
        _dataAvailableSignaled = true;
      }

      Logger.Log(LogLevel.Debug, $"StreamBuffer write: {data.Length} bytes, total: {_ringBuffer.Count}");
    }
  }

  /// <inheritdoc />
  public int Read(Span<byte> destination)
  {
    lock (_locker)
    {
      if (_ringBuffer.Count == 0)
      {
        DrainSemaphore();
        return 0;
      }

      var length = Math.Min(_ringBuffer.Count, destination.Length);
      _ringBuffer.WrittenSpan[..length].CopyTo(destination);
      _ringBuffer.RemoveFirst(length);

      if (_ringBuffer.Count > 0 && !_dataAvailableSignaled)
      {
        _dataAvailable.Release();
        _dataAvailableSignaled = true;
      }

      return length;
    }
  }

  private void DrainSemaphore()
  {
    while (_dataAvailable.CurrentCount > 0 && _dataAvailable.Wait(0)) { }
    _dataAvailableSignaled = false;
  }

  /// <inheritdoc />
  public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct = default)
  {
    if (_isDisposed != 0) throw new SbModbusException("Stream has been disposed");

    while (true)
    {
      var length = Read(destination.Span);
      if (length > 0) return length;

      CancellationToken waitToken;
      if (ct.CanBeCanceled)
      {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        waitToken = linkedCts.Token;
        try
        {
          await _dataAvailable.WaitAsync(waitToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          if (ct.IsCancellationRequested) throw;
          return 0;
        }
        finally
        {
          linkedCts.Dispose();
        }
      }
      else
      {
        try
        {
          await _dataAvailable.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          return 0;
        }
      }
    }
  }

  /// <inheritdoc />
  public void Clear()
  {
    lock (_locker)
    {
      var cleared = _ringBuffer.Count;
      _ringBuffer.Clear();
      DrainSemaphore();
      if (cleared > 0)
        Logger.Log(LogLevel.Debug, $"StreamBuffer cleared, discarded {cleared} bytes");
    }
  }

  /// <inheritdoc />
  /// <remarks>
  ///   返回的 <see cref="BufferLock" /> 构造时自动上锁，Dispose 时自动解锁。
  ///   调用方直接使用 <c>using var lb = buffer.AcquireLock();</c> 即可。
  /// </remarks>
  public BufferLock AcquireLock()
  {
    Monitor.Enter(_locker);
    return new BufferLock(_ringBuffer, _locker);
  }
}
