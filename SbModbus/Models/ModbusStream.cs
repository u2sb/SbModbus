using System;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Threading;
using SbModbus.Utils;

namespace SbModbus.Transport;

/// <inheritdoc />
public abstract class ModbusStream : IModbusStream
{
  /// <summary>
  ///   内部流数据缓冲区（默认基于环形缓冲区 + SemaphoreSlim）。
  ///   子类可通过构造函数注入不同实现以进行测试。
  /// </summary>
  private readonly IStreamBuffer _buffer;

  /// <summary>
  /// </summary>
  protected ModbusStream()
    : this(new RingBufferStreamBuffer(2048))
  {
  }

  /// <summary>
  ///   注入自定义缓冲区的构造（供测试或扩展使用）。
  /// </summary>
  /// <param name="buffer">IStreamBuffer 实现</param>
  internal ModbusStream(IStreamBuffer buffer)
  {
    _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
  }

  /// <inheritdoc />
  public virtual void Dispose()
  {
    if (Interlocked.CompareExchange(ref _isDisposedInt, 1, 0) != 0) return;
    _disposeCts.Cancel();
    OnConnectStateChanged = null;
    OnDataReceived = null;
    OnDataSent = null;
    _buffer.Dispose();
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
    _buffer.Dispose();
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
  ///   异步锁 — 保护一次完整的请求-响应事务。
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
  ///   清除读缓存 — 丢弃所有已缓冲但未消费的数据。
  ///   支持 CancellationToken：取消时立即返回。
  /// </summary>
  protected virtual ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    _buffer.Clear();
#if NET8_0_OR_GREATER
    return ValueTask.CompletedTask;
#else
    return default;
#endif
  }

  /// <summary>
  ///   异步写入数据到物理传输。
  ///   子类必须实现。
  /// </summary>
  protected abstract ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);

  /// <summary>
  ///   从缓冲区中异步读取数据。阻塞直到 >= 1 字节可用、释放或超时。
  ///   保证：要么返回正数，要么抛异常（取消/超时），不再返回 0。
  /// </summary>
  protected virtual async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    if (_isDisposedInt != 0) throw new SbModbusException("Stream has been disposed");

    // _buffer.ReadAsync 内部处理等待逻辑 — 不再需要外部 while(true) + Task.Yield
    var read = await _buffer.ReadAsync(buffer, ct).ConfigureAwait(false);
    if (read > 0) return read;

    // 已释放
    throw new SbModbusException("Stream has been disposed");
  }

  /// <summary>
  ///   锁定的读写流 — 持有 AsyncLock，保证事务内独占流访问。
  /// </summary>
  public readonly struct LockedModbusStream : IDisposable
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
    public ValueTask ClearReadBufferAsync(CancellationToken ct = default)
    {
      return _modbusStream.ClearReadBufferAsync(ct);
    }

    /// <summary>
    ///   异步写入数据
    /// </summary>
    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
      return _modbusStream.WriteAsync(buffer, ct);
    }

    /// <summary>
    ///   异步读取数据 — 阻塞直到数据可用。
    ///   不再返回 0（除非 stream 已释放）。
    /// </summary>
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
  ///   连接状态发生变化时调用。
  /// </summary>
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
  ///   收到数据时触发（子类在 <see cref="WriteBuffer" /> 后可调用此方法让外部感知）。
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
  ///   发送数据时触发。
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

  #region 缓冲区访问（向后兼容 + 新接口）

  private readonly CancellationTokenSource _disposeCts = new();
  private int _isDisposedInt;

  /// <summary>
  ///   向缓冲区写入数据，若 <see cref="AutoReceive" /> 为 true 则触发 <see cref="OnDataReceived" />。
  ///   子类的接收循环调用此方法馈入数据。
  /// </summary>
  /// <param name="source">接收到的原始字节</param>
  protected void WriteBuffer(ReadOnlySpan<byte> source)
  {
    if (source.IsEmpty) return;

    _buffer.Write(source);

    // AutoReceive 模式下触发数据事件（仅在有订阅者时分配）
    if (AutoReceive && OnDataReceived != null)
      DataReceived(source.ToArray());
  }

  /// <summary>
  ///   获取带锁保护的缓冲区句柄，用于零拷贝帧解析。
  ///   构造时自动上锁，Dispose 时自动解锁。调用方使用 using 块即可。
  /// </summary>
  public BufferLock GetLockedBuffer()
  {
    return _buffer.AcquireLock();
  }

  #endregion
}
