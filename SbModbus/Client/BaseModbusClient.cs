// https://raw.githubusercontent.com/Apollo3zehn/FluentModbus/refs/heads/dev/src/FluentModbus/Client/ModbusClient.cs

using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Client;

/// <summary>
///   ModbusClient基础类
/// </summary>
public abstract class BaseModbusClient : IModbusClient
{
  /// <summary>
  /// </summary>
  /// <param name="stream"></param>
  protected BaseModbusClient(Stream stream)
  {
    Stream = stream;
    ReadTimeout = Stream.ReadTimeout;
    WriteTimeout = Stream.WriteTimeout;
  }

  /// <summary>
  ///   读超时时间
  /// </summary>
  public int ReadTimeout { get; set; }

  /// <summary>
  ///   写超时时间
  /// </summary>
  public int WriteTimeout { get; set; }

  /// <inheritdoc />
  public Stream Stream { get; }

  /// <inheritdoc />
  public bool IsConnected => Stream.CanRead;

  /// <inheritdoc />
  public BigAndSmallEndianEncodingMode EncodingMode { get; set; } = BigAndSmallEndianEncodingMode.DCBA;
  

  #region 通用公共方法

  /// <summary>
  ///   错误
  /// </summary>
  /// <param name="functionCode"></param>
  /// <param name="exceptionCode"></param>
  /// <exception cref="ModbusException"></exception>
  protected void ProcessError(ModbusFunctionCode functionCode, ModbusExceptionCode exceptionCode)
  {
    switch (exceptionCode)
    {
      case ModbusExceptionCode.OK:
        break;

      case ModbusExceptionCode.IllegalFunction:
        throw new ModbusException(exceptionCode,
          "The function code received in the query is not an allowable action for the server. This may be because the function code is only applicable to newer devices, and was not implemented in the unit selected. It could also indicate that the server is in the wrong state to process a request of this type, for example because it is unconfigured and is being asked to return register values.");

      case ModbusExceptionCode.IllegalDataAddress:
        throw new ModbusException(exceptionCode,
          "The data address received in the query is not an allowable address for the server. More specifically, the combination of reference number and transfer length is invalid");

      case ModbusExceptionCode.IllegalDataValue:
        throw functionCode switch
        {
          ModbusFunctionCode.WriteMultipleRegisters => new ModbusException(exceptionCode,
            "The quantity of registers is out of range (1..123). Make sure to request a minimum of one register. If you use the generic overload methods, please note that a single register consists of 2 bytes. If, for example, 1 x int32 value is requested, this results in a read operation of 2 registers."),
          ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters => new ModbusException(
            exceptionCode,
            "The quantity of registers is out of range (1..125). Make sure to request a minimum of one register. If you use the generic overload methods, please note that a single register consists of 2 bytes. If, for example, 1 x int32 value is requested, this results in a read operation of 2 registers."),
          ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs => new ModbusException(exceptionCode,
            "The quantity of coils is out of range (1..2000)."),
          _ => new ModbusException(exceptionCode,
            "A value contained in the query data field is not an allowable value for server. This indicates a fault in the structure of the remainder of a complex request, such as that the implied length is incorrect.")
        };

      case ModbusExceptionCode.ServerDeviceFailure:
        throw new ModbusException(exceptionCode,
          "An unrecoverable error occurred while the server was attempting to perform the requested action.");

      case ModbusExceptionCode.Acknowledge:
        throw new ModbusException(exceptionCode,
          "The server has accepted the request and is processing it, but a long duration of time will be required to do so.");

      case ModbusExceptionCode.ServerDeviceBusy:
        throw new ModbusException(exceptionCode,
          "The server is engaged in processing a long–duration program command.");

      case ModbusExceptionCode.MemoryParityError:
        throw new ModbusException(exceptionCode,
          "The server attempted to read record file, but detected a parity error in the memory.");

      case ModbusExceptionCode.GatewayPathUnavailable:
        throw new ModbusException(exceptionCode,
          "The gateway was unable to allocate an internal communication path from the input port to the output port for processing the request.");

      case ModbusExceptionCode.GatewayTargetDeviceFailedToRespond:
        throw new ModbusException(exceptionCode, "No response was obtained from the target device");

      default:
        throw new ModbusException(exceptionCode,
          $"Unknown {(int)exceptionCode} Modbus error received.");
    }
  }

  /// <summary>
  ///   转换到 byte
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <returns></returns>
  /// <exception cref="Exception"></exception>
  protected byte ConvertByte(int unitIdentifier)
  {
    if (unitIdentifier is < 0 or > byte.MaxValue)
      throw new Exception(
        "The unit identifier is invalid. Valid node addresses are in the range of 0 - 247. Use address '0' to broadcast write command to all available servers.");

    return unchecked((byte)unitIdentifier);
  }

  /// <summary>
  ///   转换到 ushort
  /// </summary>
  /// <param name="value"></param>
  /// <returns></returns>
  /// <exception cref="Exception"></exception>
  protected ushort ConvertUshort(int value)
  {
    if (value is < 0 or > ushort.MaxValue)
      throw new Exception("The value is invalid. Valid values are in the range of 0 - 65535.");

    return unchecked((ushort)value);
  }

  #endregion

  #region 子类需要实现的方法

  /// <summary>
  ///   创建数据帧
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="functionCode"></param>
  /// <param name="startingAddress"></param>
  /// <param name="initialCapacity"></param>
  /// <returns></returns>
  protected abstract ArrayBufferWriter<byte> CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, int initialCapacity = 8);

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="data">要写入的数据</param>
  /// <param name="length">需要读取的数据长度</param>
  /// <param name="initialTimeout">超时时间 单位ms</param>
  /// <returns></returns>
  protected Span<byte> WriteAndReadWithTimeout(ReadOnlySpan<byte> data, int length, int initialTimeout)
  {
    return WriteAndReadWithTimeoutAsync(data.ToMemory<byte, byte>(), length, initialTimeout).Result.Span;
  }

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="data">要写入的数据</param>
  /// <param name="length">需要读取的数据长度</param>
  /// <param name="initialTimeout">超时时间 单位ms</param>
  /// <returns></returns>
  /// <exception cref="ModbusException"></exception>
  protected abstract ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data, int length, int initialTimeout);

  /// <summary>
  ///   读寄存器通用方法
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="functionCode"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  protected abstract Span<ushort> ReadRegisters(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, int count);

  /// <summary>
  ///   异步读取寄存器通用方法
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="functionCode"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  protected abstract ValueTask<Memory<ushort>> ReadRegistersAsync(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, int count);


  /// <summary>
  ///   校验返回的数据帧
  /// </summary>
  /// <param name="data"></param>
  protected abstract void VerifyFrame(ReadOnlySpan<byte> data);

  #endregion

  #region Modbus具体实现

  /// <inheritdoc />
  public abstract BitArray ReadCoils(int unitIdentifier, int startingAddress, int count);

  /// <inheritdoc />
  public abstract ValueTask<BitArray> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count);


  /// <inheritdoc />
  public Span<ushort> ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count)
  {
    return ReadRegisters(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress, count);
  }

  /// <inheritdoc />
  public async ValueTask<Memory<ushort>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress, int count)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress, count);
  }


  /// <inheritdoc />
  public Span<ushort> ReadHoldingRegisters(int unitIdentifier, int startingAddress, int count)
  {
    return ReadRegisters(unitIdentifier, ModbusFunctionCode.ReadHoldingRegisters, startingAddress, count);
  }

  /// <inheritdoc />
  public async ValueTask<Memory<ushort>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadHoldingRegisters, startingAddress, count);
  }


  /// <inheritdoc />
  public Span<ushort> ReadInputRegisters(int unitIdentifier, int startingAddress, int count)
  {
    return ReadRegisters(unitIdentifier, ModbusFunctionCode.ReadInputRegisters, startingAddress, count);
  }

  /// <inheritdoc />
  public async ValueTask<Memory<ushort>> ReadInputRegistersAsync(int unitIdentifier, int startingAddress, int count)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadInputRegisters, startingAddress, count);
  }

  /// <inheritdoc />
  public abstract void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value);

  /// <inheritdoc />
  public abstract ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value);


  /// <inheritdoc />
  public abstract void WriteSingleRegister(int unitIdentifier, int startingAddress, ushort value);

  /// <inheritdoc />
  public abstract ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ushort value);


  /// <inheritdoc />
  public void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<ushort> data)
  {
    WriteMultipleRegisters(unitIdentifier, startingAddress, data.AsBytes());
  }

  /// <inheritdoc />
  public abstract void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data);

  /// <inheritdoc />
  public async ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, Memory<ushort> data)
  {
    await WriteMultipleRegistersAsync(unitIdentifier, startingAddress, data.AsBytes());
  }

  /// <inheritdoc />
  public abstract ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, Memory<byte> data);

  #endregion
}