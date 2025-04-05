// https://raw.githubusercontent.com/Apollo3zehn/FluentModbus/refs/heads/dev/src/FluentModbus/Client/ModbusClient.cs

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;
using SbModbus.Properties;
using SbModbus.Services;

namespace SbModbus.ModbusClient;

/// <summary>
///   ModbusClient基础类
/// </summary>
public abstract class BaseModbusClient : IModbusClient
{
  /// <summary>
  ///   清除读缓存
  /// </summary>
  public Func<Stream, CancellationToken, Task>? ClearReadBufferAsync;

  /// <summary>
  /// </summary>
  /// <param name="stream"></param>
  protected BaseModbusClient(IModbusStream stream)
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
  public IModbusStream Stream { get; }

  /// <inheritdoc />
  public bool IsConnected => Stream.IsConnected;


  #region 通用公共方法

  /// <summary>
  ///   错误
  /// </summary>
  /// <param name="functionCode"></param>
  /// <param name="exceptionCode"></param>
  /// <exception cref="SbModbusException"></exception>
  protected void ProcessError(ModbusFunctionCode functionCode, ModbusExceptionCode exceptionCode)
  {
    switch (exceptionCode)
    {
      case ModbusExceptionCode.IllegalFunction:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x01_IllegalFunction);

      case ModbusExceptionCode.IllegalDataAddress:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x02_IllegalDataAddress);

      case ModbusExceptionCode.IllegalDataValue:
        throw functionCode switch
        {
          ModbusFunctionCode.WriteMultipleRegisters => new SbModbusException(exceptionCode,
            ErrorMessage.ModbusClient_0x03_IllegalDataValue_0x7B),
          ModbusFunctionCode.ReadHoldingRegisters or ModbusFunctionCode.ReadInputRegisters => new SbModbusException(
            exceptionCode, ErrorMessage.ModbusClient_0x03_IllegalDataValue_0x7D),
          ModbusFunctionCode.ReadCoils or ModbusFunctionCode.ReadDiscreteInputs => new SbModbusException(exceptionCode,
            ErrorMessage.ModbusClient_0x03_IllegalDataValue_0x7D0),
          _ => new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x03_IllegalDataValue)
        };

      case ModbusExceptionCode.ServerDeviceFailure:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x04_ServerDeviceFailure);

      case ModbusExceptionCode.Acknowledge:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x05_Acknowledge);

      case ModbusExceptionCode.ServerDeviceBusy:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x06_ServerDeviceBusy);

      case ModbusExceptionCode.MemoryParityError:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x08_MemoryParityError);

      case ModbusExceptionCode.GatewayPathUnavailable:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x0A_GatewayPathUnavailable);

      case ModbusExceptionCode.GatewayTargetDeviceFailedToRespond:
        throw new SbModbusException(exceptionCode, ErrorMessage.ModbusClient_0x0B_GatewayTargetDeviceFailedToRespond);

      default:
        throw new SbModbusException(exceptionCode,
          string.Format(ErrorMessage.ModbusClient_Unknown_Error, (int)exceptionCode));
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
      throw new Exception(ErrorMessage.ModbusClient_InvalidUnitIdentifier);

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
      throw new Exception(ErrorMessage.Modbus_InvalidValueUShort);

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
  /// <param name="data"></param>
  /// <param name="extendFrame"></param>
  /// <returns></returns>
  protected abstract ArrayBufferWriter<byte> CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, ReadOnlySpan<byte> data, Action<ArrayBufferWriter<byte>>? extendFrame);

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="data">要写入的数据</param>
  /// <param name="length">需要读取的数据长度</param>
  /// <param name="readTimeout">超时时间 单位ms</param>
  /// <returns></returns>
  protected Span<byte> WriteAndReadWithTimeout(ReadOnlySpan<byte> data, int length, int readTimeout)
  {
    return WriteAndReadWithTimeoutAsync(new ReadOnlyMemory<byte>(data.ToArray()), length, readTimeout,
      CancellationToken.None).Result.Span;
  }

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
    int readTimeout, CancellationToken ct);

  /// <summary>
  ///   读寄存器通用方法
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="functionCode"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  protected abstract Span<byte> ReadRegisters(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, int count);

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
    int startingAddress, int count, CancellationToken ct);


  /// <summary>
  ///   校验返回的数据帧
  /// </summary>
  /// <param name="data"></param>
  protected abstract void VerifyFrame(Span<byte> data);

  #endregion

  #region Modbus具体实现

  /// <inheritdoc />
  public abstract Span<byte> ReadCoils(int unitIdentifier, int startingAddress, int count);

  /// <inheritdoc />
  public abstract ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct);


  /// <inheritdoc />
  public abstract Span<byte> ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count);

  /// <inheritdoc />
  public abstract ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct);


  /// <inheritdoc />
  public Span<byte> ReadHoldingRegisters(int unitIdentifier, int startingAddress, int count)
  {
    return ReadRegisters(unitIdentifier, ModbusFunctionCode.ReadHoldingRegisters, startingAddress, count);
  }

  /// <inheritdoc />
  public async ValueTask<Memory<byte>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadHoldingRegisters, startingAddress, count,
      ct);
  }


  /// <inheritdoc />
  public Span<byte> ReadInputRegisters(int unitIdentifier, int startingAddress, int count)
  {
    return ReadRegisters(unitIdentifier, ModbusFunctionCode.ReadInputRegisters, startingAddress, count);
  }

  /// <inheritdoc />
  public async ValueTask<Memory<byte>> ReadInputRegistersAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct)
  {
    return await ReadRegistersAsync(unitIdentifier, ModbusFunctionCode.ReadInputRegisters, startingAddress, count, ct);
  }

  /// <inheritdoc />
  public abstract void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value);

  /// <inheritdoc />
  public abstract ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value,
    CancellationToken ct);


  /// <inheritdoc />
  public abstract void WriteSingleRegister(int unitIdentifier, int startingAddress, ushort value);

  /// <inheritdoc />
  public abstract ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ushort value,
    CancellationToken ct);

  /// <inheritdoc />
  public abstract void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data);


  /// <inheritdoc />
  public abstract ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct);

  #endregion
}