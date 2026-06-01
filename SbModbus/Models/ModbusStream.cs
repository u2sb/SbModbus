using System;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Buffers.RingBuffers;
using Sb.Extensions.System.Threading;

namespace SbModbus.Models;

/// <inheritdoc />
public abstract class ModbusStream : IModbusStream
{
  /// <summary>
  /// </summary>
  protected ModbusStream()
  {
  }

  /// <inheritdoc />
  public virtual void Dispose()
  {
    if (Interlocked.CompareExchange(ref _isDisposedInt, 1, 0) != 0) return;
    _disposeCts.Cancel();
    OnConnectStateChanged = null;
    OnDataReceived = null;
    OnDataSent = null;
    _dataAvailable.Dispose();
    _disposeCts.Dispose();
    StreamLock.Dispose();
    Logger.Information("ModbusStream disposed");
  }

  /// <inheritdoc />
  public virtual async ValueTask DisposeAsync()
  {
    if (Interlocked.CompareExchange(ref _isDisposedInt, 1, 0) != 0) return;
    _disposeCts.Cancel();
    OnConnectStateChanged = null;
    OnDataReceived = null;
    OnDataSent = null;
    _dataAvailable.Dispose();
    _disposeCts.Dispose();
    StreamLock.Dispose();
    Logger.Information("ModbusStream disposed async");
    await Task.CompletedTask;
  }

  /// <inheritdoc />
  public bool IsDisposed => _isDisposedInt != 0;

  /// <inheritdoc />
  public bool AutoReceive { get; set; }

  /// <inheritdoc />
  public abstract bool IsConnected { get; }

  /// <inheritdoc />
  public abstract int ReadTimeout { get; set; }

  /// <inheritdoc />
  public abstract int WriteTimeout { get; set; }

  /// <summary>
  ///   异步锁
  /// </summary>
  public AsyncLock StreamLock { get; } = new();

  /// <inheritdoc />
  public abstract bool Connect();

  /// <inheritdoc />
  public abstract bool Disconnect();

  /// <inheritdoc />
  public virtual Task<bool> ConnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(Connect());
  }

  /// <inheritdoc />
  public virtual Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(Disconnect());
  }

  /// <inheritdoc />
  public virtual string GetTransportInfo()
  {
    return string.Empty;
  }

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
  protected virtual async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    if (_isDisposedInt != 0) throw new SbModbusException("Stream has been disposed");

    while (true)
    {
      var length = ReadBuffer(buffer.Span);
      if (length > 0) return length;

      if (ct.CanBeCanceled)
      {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        // ReSharper disable once InconsistentlySynchronizedField
        await _dataAvailable.WaitAsync(linkedCts.Token).ConfigureAwait(false);
      }
      else
      {
        // dispose 时会 Cancel _disposeCts，WaitAsync 抛 OperationCanceledException 退出
        // ReSharper disable once InconsistentlySynchronizedField
        await _dataAvailable.WaitAsync(_disposeCts.Token).ConfigureAwait(false);
      }
    }
  }

  /// <summary>
  ///   锁定的读写流
  /// </summary>
  public struct LockedModbusStream : IDisposable
  {
    private readonly AsyncLock.InnerLock _lock;

    private readonly ModbusStream _modbusStream;

    private LockedModbusStream(ModbusStream modbusStream, AsyncLock.InnerLock l)
    {
      _modbusStream = modbusStream;
      _lock = l;
    }

    internal static LockedModbusStream Lock(ModbusStream modbusStream)
    {
      var l = modbusStream.StreamLock.Lock();
      return new LockedModbusStream(modbusStream, l);
    }

    internal static async Task<LockedModbusStream> LockAsync(ModbusStream modbusStream,
      CancellationToken ct = default)
    {
      var l = await modbusStream.StreamLock.LockAsync(ct);
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
  public event ModbusStreamDataHandler? OnDataReceived;

  /// <inheritdoc />
  public event ModbusStreamDataHandler? OnDataSent;

  /// <inheritdoc />
  public event Action<IModbusStream, bool>? OnConnectStateChanged;

  /// <summary>
  ///   连接状态发生变化时
  /// </summary>
  /// <param name="isConnected"></param>
  protected void ConnectStateChanged(bool isConnected)
  {
    Logger.Log(LogLevel.Information, $"Connection state changed: {(isConnected ? "Connected" : "Disconnected")}");
    try
    {
      OnConnectStateChanged?.Invoke(this, isConnected);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnConnectStateChanged callback threw an exception");
    }
  }

  /// <summary>
  ///   收到数据时触发（子类在 WriteBuffer 后可调用此方法让外部感知）
  /// </summary>
  protected void DataReceived(ReadOnlyMemory<byte> data)
  {
    Logger.Log(LogLevel.Debug, $"Data received: {SbModbusLogger.ToHexString(data.Span)}");
    try
    {
      OnDataReceived?.Invoke(this, data);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnDataReceived callback threw an exception");
    }
  }

  /// <summary>
  ///   发送数据时触发
  /// </summary>
  protected void DataSent(ReadOnlyMemory<byte> data)
  {
    Logger.Log(LogLevel.Debug, $"Data sent: {SbModbusLogger.ToHexString(data.Span)}");
    try
    {
      OnDataSent?.Invoke(this, data);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnDataSent callback threw an exception");
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
  private readonly CancellationTokenSource _disposeCts = new();
  private int _isDisposedInt;
  private bool _dataAvailableSignaled;


  /// <summary>
  ///   缓冲区
  /// </summary>
  protected readonly FixedSizeRingBuffer<byte> Buffer = new(2048);

  /// <summary>
  ///   向缓冲区内写数据，若 AutoReceive 为 true 则触发 OnDataReceived
  /// </summary>
  /// <param name="source">原始接收数据</param>
  protected void WriteBuffer(ReadOnlySpan<byte> source)
  {
    lock (_locker)
    {
      if (_isDisposedInt != 0)
      {
        Logger.Warning("Attempted to write buffer after dispose, ignoring");
        return;
      }

      Buffer.AddLastRange(source);
      if (source.Length > 0 && !_dataAvailableSignaled)
      {
        _dataAvailable.Release();
        _dataAvailableSignaled = true;
      }

      Logger.Log(LogLevel.Debug, $"Buffer write: {source.Length} bytes, total buffered: {Buffer.Count}");
    }

    // AutoReceive 模式下触发数据事件（仅在有订阅者时分配）
    if (AutoReceive && source.Length > 0 && OnDataReceived != null) DataReceived(source.ToArray());
  }

  /// <summary>
  ///   清除缓存
  /// </summary>
  protected void ClearBuffer()
  {
    lock (_locker)
    {
      var cleared = Buffer.Count;
      Buffer.Clear();
      while (_dataAvailable.CurrentCount > 0 && _dataAvailable.Wait(0))
      {
      }

      _dataAvailableSignaled = false;
      if (cleared > 0)
        Logger.Log(LogLevel.Debug, $"Buffer cleared, discarded {cleared} bytes");
    }
  }

  /// <summary>
  ///   读缓冲区
  /// </summary>
  /// <param name="span"></param>
  /// <returns></returns>
  protected int ReadBuffer(in Span<byte> span)
  {
    lock (_locker)
    {
      if (Buffer.Count == 0)
      {
        while (_dataAvailable.CurrentCount > 0 && _dataAvailable.Wait(0))
        {
        }

        _dataAvailableSignaled = false;
        return 0;
      }

      var length = Math.Min(Buffer.Count, span.Length);

      Buffer.WrittenSpan[..length].CopyTo(span);
      Buffer.RemoveFirst(length);

      if (Buffer.Count > 0 && !_dataAvailableSignaled)
      {
        _dataAvailable.Release();
        _dataAvailableSignaled = true;
      }

      return length;
    }
  }

  /// <summary>
  ///   获取锁定后的缓冲区，用于外部直接解析帧。
  ///   在 using 块内可安全操作 <see cref="FixedSizeRingBuffer{T}" />。
  /// </summary>
  public LockedBuffer GetLockedBuffer()
  {
    return new LockedBuffer(Buffer, _locker);
  }

  /// <summary>
  ///   带锁保护的缓冲区句柄（ref struct，确保栈上分配）。
  ///   持有 <see cref="FixedSizeRingBuffer{T}" /> 引用，Dispose 时自动释放锁。
  /// </summary>
  public ref struct LockedBuffer : IDisposable
  {
    /// <summary>
    ///   受锁保护的环形缓冲区
    /// </summary>
    public readonly FixedSizeRingBuffer<byte> Buffer;

#if NET10_0_OR_GREATER
    private Lock.Scope _scope;

    internal LockedBuffer(FixedSizeRingBuffer<byte> buffer, Lock locker)
    {
      Buffer = buffer;
      _scope = locker.EnterScope();
    }

    /// <inheritdoc />
    public void Dispose()
    {
      _scope.Dispose();
    }
#else
    private readonly object _locker;
    private bool _disposed;

    internal LockedBuffer(FixedSizeRingBuffer<byte> buffer, object locker)
    {
      Buffer = buffer;
      _locker = locker;
      _disposed = false;
      Monitor.Enter(_locker);
    }

    /// <inheritdoc />
    public void Dispose()
    {
      if (!_disposed)
      {
        Monitor.Exit(_locker);
        _disposed = true;
      }
    }
#endif
  }

  #endregion
}
