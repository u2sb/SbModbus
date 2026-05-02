using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System;
using SbModbus.Services.ModbusClient;
using SbModbus.Utils;

namespace SbModbus.Models;

/// <summary>
///   Modbus请求
/// </summary>
public class ModbusRequest
{
  private ModbusRequest()
  {
  }

  /// <summary>
  ///   从站地址
  /// </summary>
  public byte SlaveId { get; set; } = 1;

  /// <summary>
  ///   功能码
  /// </summary>
  public ModbusFunctionCode FunctionCode { get; set; }

  /// <summary>
  ///   地址
  ///   0-based
  /// </summary>
  public ushort StartingAddress { get; set; }

  /// <summary>
  ///   读请求数量
  /// </summary>
  public ushort Quantity { get; set; } = 1;

  /// <summary>
  ///   数据
  /// </summary>
  public byte[] Data { get; set; } = [];

  /// <summary>
  ///   返回值
  /// </summary>
  public ModbusResponse Response { get; set; } = new();

  /// <summary>
  ///   重置
  /// </summary>
  public void Reset()
  {
    Response = new ModbusResponse();
  }

  /// <summary>
  ///   发送请求
  /// </summary>
  /// <param name="client"></param>
  public ModbusResponse Send(IModbusClient client)
  {
    return SbThreading.Jtf.Run(() => SendAsync(client));
  }

  /// <summary>
  ///   发送请求
  /// </summary>
  /// <param name="client"></param>
  /// <param name="ct"></param>
  public async Task<ModbusResponse> SendAsync(IModbusClient client, CancellationToken ct = default)
  {
    Reset();

    if (client is null) throw new ArgumentNullException(nameof(client));

    Validate();

    Response.Status = ModbusResponse.ResponseStatus.Requesting;

    try
    {
      switch (FunctionCode)
      {
        case ModbusFunctionCode.ReadCoils:
          Response.Data = (await client.ReadCoilsAsync(SlaveId, StartingAddress, Quantity, ct).ConfigureAwait(false))
            .ToArray();
          break;
        case ModbusFunctionCode.ReadDiscreteInputs:
          Response.Data =
            (await client.ReadDiscreteInputsAsync(SlaveId, StartingAddress, Quantity, ct).ConfigureAwait(false))
            .ToArray();
          break;
        case ModbusFunctionCode.ReadHoldingRegisters:
          Response.Data =
            (await client.ReadHoldingRegistersAsync(SlaveId, StartingAddress, Quantity, ct).ConfigureAwait(false))
            .ToArray();
          break;
        case ModbusFunctionCode.ReadInputRegisters:
          Response.Data =
            (await client.ReadInputRegistersAsync(SlaveId, StartingAddress, Quantity, ct).ConfigureAwait(false))
            .ToArray();
          break;
        case ModbusFunctionCode.WriteSingleCoil:
          await client
            .WriteSingleCoilAsync(SlaveId, StartingAddress, Data, ct)
            .ConfigureAwait(false);
          break;
        case ModbusFunctionCode.WriteSingleRegister:
          await client.WriteSingleRegisterAsync(SlaveId, StartingAddress, Data, ct).ConfigureAwait(false);
          break;
        case ModbusFunctionCode.WriteMultipleRegisters:
          await client.WriteMultipleRegistersAsync(SlaveId, StartingAddress, Data, ct).ConfigureAwait(false);
          break;
        case ModbusFunctionCode.WriteMultipleCoils:
          throw new NotSupportedException(
            $"Function code '{FunctionCode}' is not supported by {nameof(IModbusClient)} currently.");
        default:
          throw new NotSupportedException(
            $"Function code '{FunctionCode}' is not supported by {nameof(ModbusRequest)}.{nameof(SendAsync)}().");
      }

      Response.Status = ModbusResponse.ResponseStatus.Success;
      return Response;
    }
    catch (Exception ex)
    {
      return ApplyException(ex);
    }
  }

  private ModbusResponse ApplyException(Exception ex)
  {
    Response.Error = ex;
    Response.Message = ex.Message;

    if (ex is SbModbusException mex) Response.ExceptionCode = mex.ExceptionCode;

    Response.Status = IsTimeout(ex)
      ? ModbusResponse.ResponseStatus.Timeout
      : ModbusResponse.ResponseStatus.Exception;

    return Response;
  }

  private static bool IsTimeout(Exception ex)
  {
    return ex is TimeoutException or OperationCanceledException
           || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
  }

  #region 静态函数

  /// <summary>
  ///   创建读取线圈请求（FC01）。
  /// </summary>
  /// <param name="startingAddress">起始地址（0-based）。</param>
  /// <param name="quantity">读取数量。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateReadCoils(ushort startingAddress, ushort quantity, byte slaveId = 1)
  {
    return CreateReadRequest(ModbusFunctionCode.ReadCoils, startingAddress, quantity, slaveId);
  }

  /// <summary>
  ///   创建读取离散输入请求（FC02）。
  /// </summary>
  /// <param name="startingAddress">起始地址（0-based）。</param>
  /// <param name="quantity">读取数量。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateReadDiscreteInputs(ushort startingAddress, ushort quantity, byte slaveId = 1)
  {
    return CreateReadRequest(ModbusFunctionCode.ReadDiscreteInputs, startingAddress, quantity, slaveId);
  }

  /// <summary>
  ///   创建读取保持寄存器请求（FC03）。
  /// </summary>
  /// <param name="startingAddress">起始地址（0-based）。</param>
  /// <param name="quantity">读取数量。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateReadHoldingRegisters(ushort startingAddress, ushort quantity, byte slaveId = 1)
  {
    return CreateReadRequest(ModbusFunctionCode.ReadHoldingRegisters, startingAddress, quantity, slaveId);
  }

  /// <summary>
  ///   创建读取输入寄存器请求（FC04）。
  /// </summary>
  /// <param name="startingAddress">起始地址（0-based）。</param>
  /// <param name="quantity">读取数量。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateReadInputRegisters(ushort startingAddress, ushort quantity, byte slaveId = 1)
  {
    return CreateReadRequest(ModbusFunctionCode.ReadInputRegisters, startingAddress, quantity, slaveId);
  }

  /// <summary>
  ///   创建写单个线圈请求（FC05）。
  /// </summary>
  /// <param name="startingAddress">线圈地址（0-based）。</param>
  /// <param name="value">线圈值，true 为 ON，false 为 OFF。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateWriteSingleCoil(ushort startingAddress, bool value, byte slaveId = 1)
  {
    var request = new ModbusRequest
    {
      SlaveId = slaveId,
      FunctionCode = ModbusFunctionCode.WriteSingleCoil,
      StartingAddress = startingAddress,
      Quantity = 1,
      Data = value ? [0xFF, 0x00] : "\0\0"u8.ToArray()
    };

    request.Validate();
    return request;
  }

  /// <summary>
  ///   创建写单个寄存器请求（FC06）。
  /// </summary>
  /// <param name="startingAddress">寄存器地址（0-based）。</param>
  /// <param name="value">寄存器值。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateWriteSingleRegister(ushort startingAddress, ushort value, byte slaveId = 1)
  {
    var request = new ModbusRequest
    {
      SlaveId = slaveId,
      FunctionCode = ModbusFunctionCode.WriteSingleRegister,
      StartingAddress = startingAddress,
      Quantity = 1,
      Data = [(byte)(value >> 8), (byte)value]
    };

    request.Validate();
    return request;
  }

  /// <summary>
  ///   创建写多个线圈请求（FC15）。
  /// </summary>
  /// <param name="startingAddress">起始线圈地址（0-based）。</param>
  /// <param name="data">线圈位数据。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateWriteMultipleCoils(ushort startingAddress, ReadOnlyBitSpan data,
    byte slaveId = 1)
  {
    var length = data.Length;

    var bytes = new byte[(length + 7) / 8];
    data.CopyTo(bytes.AsSpan(), 0, length);

    var request = new ModbusRequest
    {
      SlaveId = slaveId,
      FunctionCode = ModbusFunctionCode.WriteMultipleCoils,
      StartingAddress = startingAddress,
      Quantity = checked((ushort)data.Length),
      Data = bytes
    };

    request.Validate();
    return request;
  }

  /// <summary>
  ///   创建写多个线圈请求（FC15）。
  /// </summary>
  /// <param name="startingAddress">起始线圈地址（0-based）。</param>
  /// <param name="data">线圈位数据。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateWriteMultipleCoils(ushort startingAddress, BitArray data,
    byte slaveId = 1)
  {
    var length = data.Length;

    var bytes = new byte[(length + 7) / 8];
    data.CopyTo(bytes, 0);

    var request = new ModbusRequest
    {
      SlaveId = slaveId,
      FunctionCode = ModbusFunctionCode.WriteMultipleCoils,
      StartingAddress = startingAddress,
      Quantity = checked((ushort)data.Length),
      Data = bytes
    };

    request.Validate();
    return request;
  }

  /// <summary>
  ///   创建写多个寄存器请求（FC16）。
  /// </summary>
  /// <param name="startingAddress">起始寄存器地址（0-based）。</param>
  /// <param name="data">按高字节在前排列的寄存器字节流。</param>
  /// <param name="slaveId">从站地址。</param>
  /// <returns>已校验的请求对象。</returns>
  public static ModbusRequest CreateWriteMultipleRegisters(ushort startingAddress, ReadOnlyMemory<byte> data,
    byte slaveId = 1)
  {
    var request = new ModbusRequest
    {
      SlaveId = slaveId,
      FunctionCode = ModbusFunctionCode.WriteMultipleRegisters,
      StartingAddress = startingAddress,
      Quantity = checked((ushort)(data.Length / 2)),
      Data = data.ToArray()
    };

    request.Validate();
    return request;
  }

  private static ModbusRequest CreateReadRequest(ModbusFunctionCode functionCode, ushort startingAddress,
    ushort quantity, byte slaveId)
  {
    var request = new ModbusRequest
    {
      SlaveId = slaveId,
      FunctionCode = functionCode,
      StartingAddress = startingAddress,
      Quantity = quantity,
      Data = []
    };

    request.Validate();
    return request;
  }

  #endregion


  #region 校验

  /// <summary>
  ///   校验请求参数是否符合功能码要求
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException"></exception>
  /// <exception cref="ArgumentException"></exception>
  /// <exception cref="NotSupportedException"></exception>
  public void Validate()
  {
    switch (FunctionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
        ValidateReadRequest(1, 2000);
        break;
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        ValidateReadRequest(1, 125);
        break;
      case ModbusFunctionCode.WriteSingleCoil:
        ValidateWriteSingleCoil();
        break;
      case ModbusFunctionCode.WriteSingleRegister:
        ValidateWriteSingleRegister();
        break;
      case ModbusFunctionCode.WriteMultipleCoils:
        ValidateWriteMultipleCoils();
        break;
      case ModbusFunctionCode.WriteMultipleRegisters:
        ValidateWriteMultipleRegisters();
        break;
      default:
        throw new NotSupportedException(
          $"Function code '{FunctionCode}' is not supported by ModbusRequest.Validate().");
    }
  }

  private void ValidateReadRequest(ushort minQuantity, ushort maxQuantity)
  {
    ValidateQuantityRange(minQuantity, maxQuantity);
    ValidateAddressRange(Quantity);
  }

  private void ValidateWriteSingleCoil()
  {
    if (Quantity != 1) throw new ArgumentOutOfRangeException(nameof(Quantity), "WriteSingleCoil quantity must be 1.");
    if (Data.Length != 2)
      throw new ArgumentException("WriteSingleCoil payload length must be exactly 2 bytes.", nameof(Data));

    var isOn = Data[0] == 0xFF && Data[1] == 0x00;
    var isOff = Data[0] == 0x00 && Data[1] == 0x00;
    if (!isOn && !isOff)
      throw new ArgumentException("WriteSingleCoil payload must be 0xFF00 (ON) or 0x0000 (OFF).", nameof(Data));
  }

  private void ValidateWriteSingleRegister()
  {
    if (Quantity != 1)
      throw new ArgumentOutOfRangeException(nameof(Quantity), "WriteSingleRegister quantity must be 1.");
    if (Data.Length != 2)
      throw new ArgumentException("WriteSingleRegister payload length must be exactly 2 bytes.", nameof(Data));
  }

  private void ValidateWriteMultipleCoils()
  {
    ValidateQuantityRange(1, 1968);
    ValidateAddressRange(Quantity);

    if (Data.Length is < 1 or > 246)
      throw new ArgumentOutOfRangeException(nameof(Data),
        "WriteMultipleCoils payload length must be in range 1..246 bytes.");

    var expectedByteCount = (Quantity + 7) / 8;
    if (Data.Length != expectedByteCount)
      throw new ArgumentException("WriteMultipleCoils payload/coil quantity mismatch.", nameof(Data));
  }

  private void ValidateWriteMultipleRegisters()
  {
    ValidateQuantityRange(1, 123);
    ValidateAddressRange(Quantity);

    if ((Data.Length & 1) != 0)
      throw new ArgumentException("WriteMultipleRegisters payload length must be even.", nameof(Data));
    if (Data.Length is < 2 or > 246)
      throw new ArgumentOutOfRangeException(nameof(Data),
        "WriteMultipleRegisters payload length must be in range 2..246 bytes.");

    var registerCount = Data.Length / 2;
    if (registerCount != Quantity)
      throw new ArgumentException("WriteMultipleRegisters payload/register quantity mismatch.", nameof(Data));
  }

  private void ValidateQuantityRange(ushort min, ushort max)
  {
    if (Quantity < min || Quantity > max)
      throw new ArgumentOutOfRangeException(nameof(Quantity),
        $"Quantity for {FunctionCode} must be in range {min}..{max}.");
  }

  private void ValidateAddressRange(ushort quantity)
  {
    var endAddress = (uint)StartingAddress + quantity - 1;
    if (endAddress > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(StartingAddress), "Address range exceeds 0..65535.");
  }

  #endregion
}
