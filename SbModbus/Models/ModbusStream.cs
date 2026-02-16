using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Threading;
#if NETSTANDARD2_0
using CommunityToolkit.HighPerformance;
#endif


namespace SbModbus.Models;

/// <inheritdoc />
public abstract class ModbusStream : IModbusStream
{
  /// <inheritdoc />
  public abstract void Dispose();

  /// <inheritdoc />
  public abstract Stream? BaseStream { get; protected set; }

  /// <inheritdoc />
  public abstract bool IsConnected { get; }

  /// <inheritdoc />
  public abstract int ReadTimeout { get; set; }

  /// <inheritdoc />
  public abstract int WriteTimeout { get; set; }

  /// <inheritdoc />
  public AsyncLock StreamLock { get; } = new();

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
  public ValueTask<LockedModbusStream> LockAsync(CancellationToken ct = default)
  {
    return LockedModbusStream.LockAsync(this, ct);
  }

  /// <summary>
  ///   清除读缓存
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected abstract ValueTask ClearReadBufferAsync(CancellationToken ct = default);

  /// <summary>
  ///   写入数据
  /// </summary>
  /// <param name="buffer"></param>
  protected virtual void Write(ReadOnlySpan<byte> buffer)
  {
    if (IsConnected && BaseStream is not null)
    {
      BaseStream.Write(buffer);
      BaseStream.Flush();
      DataSent(buffer);
    }
  }

  /// <summary>
  ///   异步写入数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected virtual async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected && BaseStream is not null)
    {
      await BaseStream.WriteAsync(buffer, ct);
      await BaseStream.FlushAsync(ct);
      DataSent(buffer.Span);
    }
  }

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <returns></returns>
  protected abstract int Read(Span<byte> buffer);

  /// <summary>
  ///   异步读取数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected abstract ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);

  /// <summary>
  ///   锁定的读写流
  /// </summary>
  public readonly struct LockedModbusStream : IDisposable
  {
    private readonly InnerLock _lock;

    private readonly ModbusStream _modbusStream;

    private LockedModbusStream(ModbusStream modbusStream, InnerLock l)
    {
      _modbusStream = modbusStream;
      _lock = l;
    }

    internal static LockedModbusStream Lock(ModbusStream modbusStream)
    {
      var l = modbusStream.StreamLock.Lock();
      return new LockedModbusStream(modbusStream, l);
    }

    internal static async ValueTask<LockedModbusStream> LockAsync(ModbusStream modbusStream,
      CancellationToken ct = default)
    {
      var l = await modbusStream.StreamLock.LockAsync(ct).ConfigureAwait(false);
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
    ///   写入数据
    /// </summary>
    /// <param name="buffer"></param>
    public void Write(ReadOnlySpan<byte> buffer)
    {
      _modbusStream.Write(buffer);
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
    ///   读取数据
    /// </summary>
    /// <param name="buffer"></param>
    /// <returns></returns>
    public int Read(Span<byte> buffer)
    {
      return _modbusStream.Read(buffer);
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
  public event ModbusStreamHandler? OnDataReceived;

  /// <inheritdoc />
  public event ModbusStreamAsyncHandler? OnDataReceivedAsync;

  /// <inheritdoc />
  public event ModbusStreamHandler? OnDataSent;

  /// <inheritdoc />
  public event ModbusStreamAsyncHandler? OnDataSentAsync;

  /// <inheritdoc />
  public event ModbusStreamStateHandler<bool>? OnConnectStateChanged;

  /// <summary>
  ///   连接状态发生变化时
  /// </summary>
  /// <param name="isConnected"></param>
  protected void ConnectStateChanged(bool isConnected)
  {
    try
    {
      OnConnectStateChanged?.Invoke(this, isConnected);
    }
    catch (Exception)
    {
      // ignore
    }
  }

  /// <summary>
  ///   接收到消息时触发
  /// </summary>
  /// <param name="data"></param>
  protected virtual void DataReceived(ReadOnlySpan<byte> data)
  {
    DataTransportAsync(OnDataReceivedAsync, data, CancellationToken.None);

    try
    {
      OnDataReceived?.Invoke(this, data);
    }
    catch (Exception)
    {
      // ignore
    }
  }

  /// <summary>
  ///   接收到消息时触发
  /// </summary>
  /// <param name="data"></param>
  protected virtual void DataSent(ReadOnlySpan<byte> data)
  {
    DataTransportAsync(OnDataSentAsync, data, CancellationToken.None);

    try
    {
      OnDataSent?.Invoke(this, data);
    }
    catch (Exception)
    {
      // ignore
    }
  }

  /// <summary>
  ///   接收或发送数据时触发（异步调用）
  /// </summary>
  /// <param name="handler"></param>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  protected virtual void DataTransportAsync(ModbusStreamAsyncHandler? handler, ReadOnlySpan<byte> data,
    CancellationToken ct)
  {
    if (handler is null) return;

    var length = data.Length;
    using var bs = MemoryPool<byte>.Shared.Rent(length);

    foreach (var invocation in handler.GetInvocationList().Cast<ModbusStreamAsyncHandler>())
      // 异步调用每个订阅者，并处理异常
      // ReSharper disable once MethodSupportsCancellation
      _ = Task.Run(async () =>
      {
        using var buffer = MemoryPool<byte>.Shared.Rent(length);
        try
        {
          bs.Memory.CopyTo(buffer.Memory);
          await invocation(this, buffer.Memory, ct);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
          // ignore
        }
      });
  }

  #endregion
}
