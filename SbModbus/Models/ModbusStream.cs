using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Sb.Extensions.System.Buffers.RingBuffers;
using SbModbus.Utils;

namespace SbModbus.Models;

/// <inheritdoc />
public abstract class ModbusStream : IModbusStream
{
  /// <inheritdoc />
  public virtual void Dispose()
  {
    OnConnectStateChanged = null;
  }

  /// <inheritdoc />
  public abstract bool IsConnected { get; }

  /// <inheritdoc />
  public abstract int ReadTimeout { get; set; }

  /// <inheritdoc />
  public abstract int WriteTimeout { get; set; }

  /// <summary>
  ///   异步锁
  /// </summary>
  public AsyncSemaphore StreamLock { get; } = new(1);

  /// <inheritdoc />
  public abstract bool Connect();

  /// <inheritdoc />
  public abstract bool Disconnect();

  /// <inheritdoc />
  public LockedModbusStream Lock()
  {
    return LockedModbusStream.Lock(this);
  }

  /// <inheritdoc />
  public Task<LockedModbusStream> LockAsync(CancellationToken ct = default)
  {
    return LockedModbusStream.LockAsync(this, ct);
  }

  /// <summary>
  ///   清除读缓存
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected virtual ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    ClearBuffer();
#if NET8_0_OR_GREATER
    return ValueTask.CompletedTask;
#else
    return default;
#endif
  }

  /// <summary>
  ///   异步写入数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);

  /// <summary>
  ///   异步读取数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected virtual ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    return ReadAsyncCoreAsync(buffer, ct);
  }

  private async ValueTask<int> ReadAsyncCoreAsync(Memory<byte> buffer, CancellationToken ct)
  {
    while (true)
    {
      var length = ReadBuffer(buffer.Span);
      if (length > 0) return length;

      await _dataAvailable.WaitAsync(ct).ConfigureAwait(false);
    }
  }

  /// <summary>
  ///   锁定的读写流
  /// </summary>
  public readonly struct LockedModbusStream : IDisposable
  {
    private readonly AsyncSemaphore.Releaser _lock;

    private readonly ModbusStream _modbusStream;

    private LockedModbusStream(ModbusStream modbusStream, AsyncSemaphore.Releaser l)
    {
      _modbusStream = modbusStream;
      _lock = l;
    }

    internal static LockedModbusStream Lock(ModbusStream modbusStream)
    {
      return SbThreading.Jtf.Run(() => LockAsync(modbusStream));
    }

    internal static async Task<LockedModbusStream> LockAsync(ModbusStream modbusStream,
      CancellationToken ct = default)
    {
      var l = await modbusStream.StreamLock.EnterAsync(ct);
      return new LockedModbusStream(modbusStream, l);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      _lock.Dispose();
    }

    #region 读写

    /// <summary>
    ///   清除读缓存
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public ValueTask ClearReadBufferAsync(CancellationToken ct = default)
    {
      return _modbusStream.ClearReadBufferAsync(ct);
    }

    /// <summary>
    ///   异步写入数据
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
      return _modbusStream.WriteAsync(buffer, ct);
    }

    /// <summary>
    ///   异步读取数据
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
      return _modbusStream.ReadAsync(buffer, ct);
    }

    #endregion
  }

  #region 事件

  /// <inheritdoc />
  public Action<bool>? OnConnectStateChanged { get; set; }

  /// <summary>
  ///   连接状态发生变化时
  /// </summary>
  /// <param name="isConnected"></param>
  protected void ConnectStateChanged(bool isConnected)
  {
    try
    {
      OnConnectStateChanged?.Invoke(isConnected);
    }
    catch (Exception)
    {
      // ignore
    }
  }

  #endregion

  #region 缓冲区

#if NET10_0_OR_GREATER
  private readonly Lock _locker = new();
#else
  private readonly object _locker = new();
#endif

  private readonly SemaphoreSlim _dataAvailable = new(0);


  /// <summary>
  ///   缓冲区
  /// </summary>
  protected readonly FixedSizeRingBuffer<byte> Buffer = new(512);

  /// <summary>
  ///   向缓冲区内写数据
  /// </summary>
  /// <param name="buffer"></param>
  protected void WriteBuffer(ReadOnlySpan<byte> buffer)
  {
    lock (_locker)
    {
      Buffer.AddLastRange(buffer);
    }

    _dataAvailable.Release();
  }

  /// <summary>
  ///   清除缓存
  /// </summary>
  protected void ClearBuffer()
  {
    lock (_locker)
    {
      Buffer.Clear();
    }

    while (_dataAvailable.CurrentCount > 0) _ = _dataAvailable.Wait(0);
  }

  /// <summary>
  ///   读缓冲区
  /// </summary>
  /// <param name="span"></param>
  /// <returns></returns>
  protected int ReadBuffer(in Span<byte> span)
  {
    // ReSharper disable once InconsistentlySynchronizedField
    if (Buffer.Count == 0) return 0;

    lock (_locker)
    {
      var length = Math.Min(Buffer.Count, span.Length);

      Buffer.WrittenSpan[..length].CopyTo(span);
      Buffer.RemoveFirst(length);
      return length;
    }
  }

  #endregion
}
