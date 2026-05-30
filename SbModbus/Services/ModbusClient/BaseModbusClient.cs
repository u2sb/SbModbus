// https://raw.githubusercontent.com/Apollo3zehn/FluentModbus/refs/heads/dev/src/FluentModbus/Client/ModbusClient.cs

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusClient;

/// <summary>
///   ModbusClient基础类
/// </summary>
public abstract class BaseModbusClient : IModbusClient
{
  /// <summary>
  /// </summary>
  /// <param name="stream"></param>
  protected BaseModbusClient(IModbusStream stream)
  {
    ModbusStream = stream;

    ModbusStream.OnConnectStateChanged -= StreamOnConnectStateChanged;
    ModbusStream.OnConnectStateChanged += StreamOnConnectStateChanged;
  }

  /// <summary>
  ///   读超时时间
  /// </summary>
  public int ReadTimeout
  {
    get => ModbusStream.ReadTimeout;
    set => ModbusStream.ReadTimeout = value;
  }

  /// <summary>
  ///   写超时时间
  /// </summary>
  public int WriteTimeout
  {
    get => ModbusStream.WriteTimeout;
    set => ModbusStream.WriteTimeout = value;
  }

  /// <inheritdoc />
  public IModbusStream ModbusStream { get; }

  /// <inheritdoc />
  public bool IsConnected => ModbusStream.IsConnected;

  /// <inheritdoc />
  public int DiffSidIntervalTime { get; set; } = 0;

  /// <inheritdoc />
  public void Dispose()
  {
    if (!ModbusStream.IsDisposed)
    {
      try
      {
        ModbusStream.OnConnectStateChanged -= StreamOnConnectStateChanged;
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "Failed to unsubscribe OnConnectStateChanged during Dispose");
      }
    }

    OnConnectStateChanged = null;
    OnDataReceived = null;
    OnDataReceivedAsync = null;
    OnDataSent = null;
    OnDataSentAsync = null;
    Logger.Information("ModbusClient disposed");
  }

  private void StreamOnConnectStateChanged(IModbusStream sender, bool b)
  {
    OnConnectStateChanged?.Invoke(this, b);
  }

  #region 事件

  /// <inheritdoc />
  public event ModbusClientHandler? OnDataReceived;

  /// <inheritdoc />
  public event ModbusClientAsyncHandler? OnDataReceivedAsync;

  /// <inheritdoc />
  public event ModbusClientHandler? OnDataSent;

  /// <inheritdoc />
  public event ModbusClientAsyncHandler? OnDataSentAsync;

  /// <inheritdoc />
  public event ModbusStreamStateHandler<bool>? OnConnectStateChanged;

  /// <summary>
  ///   接收到消息时触发
  /// </summary>
  /// <param name="data"></param>
  protected async ValueTask DataReceivedAsync(ReadOnlyMemory<byte> data)
  {
    Logger.Log(LogLevel.Debug, $"Response received: {SbModbusLogger.ToHexString(data.Span)}");

    try
    {
      OnDataReceived?.Invoke(this, data);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnDataReceived callback threw an exception");
    }

    if (OnDataReceivedAsync is null) return;

    try
    {
      await OnDataTransportAsync(OnDataReceivedAsync, data).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnDataReceivedAsync callback threw an exception");
    }
  }

  /// <summary>
  ///   发送消息时触发
  /// </summary>
  /// <param name="data"></param>
  protected async ValueTask DataSentAsync(ReadOnlyMemory<byte> data)
  {
    Logger.Log(LogLevel.Debug, $"Request sent: {SbModbusLogger.ToHexString(data.Span)}");

    try
    {
      OnDataSent?.Invoke(this, data);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnDataSent callback threw an exception");
    }

    if (OnDataSentAsync is null) return;

    try
    {
      await OnDataTransportAsync(OnDataSentAsync, data).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnDataSentAsync callback threw an exception");
    }
  }


  /// <summary>
  ///   异步消息
  /// </summary>
  /// <param name="handler"></param>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  protected virtual async ValueTask OnDataTransportAsync(ModbusClientAsyncHandler handler, ReadOnlyMemory<byte> data,
    CancellationToken ct = default)
  {
    foreach (var d in handler.GetInvocationList())
    {
      var h = (ModbusClientAsyncHandler)d;
      try
      {
        await h.Invoke(this, data, ct).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "Async event handler threw an exception");
      }
    }
  }

  private static async Task InvokeHandlerAsync(ModbusClientAsyncHandler handler, IModbusClient sender, ReadOnlyMemory<byte> data, CancellationToken ct)
  {
    try
    {
      await handler.Invoke(sender, data, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "Async event handler threw an exception");
    }
  }

  #endregion


  #region 通用公共方法

  /// <summary>
  ///   错误
  /// </summary>
  /// <param name="functionCode"></param>
  /// <param name="exceptionCode"></param>
  /// <exception cref="SbModbusException"></exception>
  protected void ProcessError(ModbusFunctionCode functionCode, ModbusExceptionCode exceptionCode)
  {
    Logger.Log(LogLevel.Error, $"Modbus protocol error: functionCode=0x{(int)functionCode:X2}, exceptionCode=0x{(int)exceptionCode:X2}");

    switch (exceptionCode)
    {
      case ModbusExceptionCode.IllegalFunction:
        SbModbusThrow.IllegalFunction(exceptionCode);
        break;
      case ModbusExceptionCode.IllegalDataAddress:
        SbModbusThrow.IllegalDataAddress(exceptionCode);
        break;
      case ModbusExceptionCode.IllegalDataValue:
        SbModbusThrow.IllegalDataValue(exceptionCode, functionCode);
        break;
      case ModbusExceptionCode.ServerDeviceFailure:
        SbModbusThrow.ServerDeviceFailure(exceptionCode);
        break;
      case ModbusExceptionCode.Acknowledge:
        SbModbusThrow.Acknowledge(exceptionCode);
        break;
      case ModbusExceptionCode.ServerDeviceBusy:
        SbModbusThrow.ServerDeviceBusy(exceptionCode);
        break;
      case ModbusExceptionCode.MemoryParityError:
        SbModbusThrow.MemoryParityError(exceptionCode);
        break;
      case ModbusExceptionCode.GatewayPathUnavailable:
        SbModbusThrow.GatewayPathUnavailable(exceptionCode);
        break;
      case ModbusExceptionCode.GatewayTargetDeviceFailedToRespond:
        SbModbusThrow.GatewayTargetDeviceFailedToRespond(exceptionCode);
        break;
      default:
        SbModbusThrow.UnknownError(exceptionCode);
        break;
    }
  }

  /// <summary>
  ///   转换到 byte
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <returns></returns>
  /// <exception cref="SbModbusException"></exception>
  protected static byte ConvertByte(int unitIdentifier)
  {
    if (unitIdentifier is < 0 or > byte.MaxValue)
      throw new SbModbusException(ErrorMessages.ModbusClient_InvalidUnitIdentifier);

    return unchecked((byte)unitIdentifier);
  }

  /// <summary>
  ///   转换到 ushort
  /// </summary>
  /// <param name="value"></param>
  /// <returns></returns>
  /// <exception cref="SbModbusException"></exception>
  protected static ushort ConvertUshort(int value)
  {
    if (value is < 0 or > ushort.MaxValue)
      throw new SbModbusException(ErrorMessages.Modbus_InvalidValueUShort);

    return unchecked((ushort)value);
  }

  /// <summary>
  ///   上一次通信结束时间
  /// </summary>
  private DateTime _previousOverTime = DateTime.UtcNow;

  /// <summary>
  ///   上一次通信的sid
  /// </summary>
  private byte _previousSid;

  /// <summary>
  ///   设置上一次通信结束时间
  /// </summary>
  protected void SetPreviousOverTime(byte sid)
  {
    _previousSid = sid;
    _previousOverTime = DateTime.UtcNow;
  }

  /// <summary>
  ///   等待通信时间
  /// </summary>
  /// <returns></returns>
  protected async ValueTask WaitDiffSidIntervalTimeAsync(byte sid)
  {
    if (DiffSidIntervalTime <= 0 || sid == _previousSid) return;

    var interval = TimeSpan.FromMilliseconds(DiffSidIntervalTime);

    var span = interval - (DateTime.UtcNow - _previousOverTime);

    if (span <= TimeSpan.Zero) return;

    Logger.Log(LogLevel.Debug, $"Waiting {span.TotalMilliseconds:F1}ms for different SID interval (previous={_previousSid}, current={sid})");
    await Task.Delay(span).ConfigureAwait(false);
  }

  #endregion

  #region 子类需要实现的方法

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="data">要写入的数据</param>
  /// <param name="length">需要读取的数据长度</param>
  /// <param name="readTimeout">超时时间 单位ms</param>
  /// <param name="ct"></param>
  /// <returns></returns>
  /// <exception cref="SbModbusException"></exception>
  protected abstract ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data, int length,
    int readTimeout, CancellationToken ct = default);

  /// <summary>
  ///   异步读取寄存器通用方法
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="functionCode"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  protected abstract ValueTask<Memory<byte>> ReadRegistersAsync(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, int count, CancellationToken ct = default);

  #endregion

  #region 校验

  /// <summary>
  ///   校验 WriteSingleCoil 数据
  /// </summary>
  /// <param name="data"></param>
  /// <exception cref="SbModbusException"></exception>
  protected static void ValidateSingleCoilData(ReadOnlyMemory<byte> data)
  {
    if (data.Length != 2)
      throw new SbModbusException("WriteSingleCoil payload length must be exactly 2 bytes.");

    var isOn = data.Span[0] == 0xFF && data.Span[1] == 0x00;
    var isOff = data.Span[0] == 0x00 && data.Span[1] == 0x00;
    if (!isOn && !isOff)
      throw new SbModbusException("WriteSingleCoil payload must be 0xFF00 (ON) or 0x0000 (OFF).");
  }

  #endregion

  #region Modbus具体实现

  /// <inheritdoc />
  public abstract ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default);


  /// <inheritdoc />
  public abstract ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default);


  /// <inheritdoc />
  public async ValueTask<Memory<byte>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadHoldingRegisters, startingAddress, count,
      ct).ConfigureAwait(false);
  }


  /// <inheritdoc />
  public async ValueTask<Memory<byte>> ReadInputRegistersAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadInputRegisters, startingAddress, count, ct)
      .ConfigureAwait(false);
  }

  /// <inheritdoc />
  public virtual ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value,
    CancellationToken ct = default)
  {
    var buffer = value ? [0xFF, 0x00] : "\0\0"u8.ToArray();
    return WriteSingleCoilAsync(unitIdentifier, startingAddress, buffer, ct);
  }

  /// <inheritdoc />
  public abstract ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);

  /// <inheritdoc />
  public abstract ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);

  /// <inheritdoc />
  public ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ushort value,
    CancellationToken ct = default)
  {
    return WriteSingleRegisterAsync(unitIdentifier, startingAddress, value.ToByteArray(), ct);
  }

  /// <inheritdoc />
  public ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, short value,
    CancellationToken ct = default)
  {
    return WriteSingleRegisterAsync(unitIdentifier, startingAddress, value.ToByteArray(), ct);
  }


  /// <inheritdoc />
  public abstract ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct = default);

  #endregion
}
