using System;
using System.Buffers;
using System.Collections;
using System.IO;
using SbModbus.Models;
using SbModbus.Utils;
using BitConverter = SbModbus.Utils.BitConverter;

namespace SbModbus.Client;

/// <summary>
///   ModbusRTU Client
/// </summary>
/// <param name="stream"></param>
public partial class ModbusRtuClient(Stream stream) : BaseModbusClient(stream), IModbusClient
{
  /// <inheritdoc />
  public override BitArray ReadCoils(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress);
    buffer.Write(ConvertUshort(count).ToBytes(true));
    buffer.WriteCrc16();

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);

    // 返回数据
    return new BitArray(result[3..^2].ToArray());
  }
  
  /// <inheritdoc />
  public override BitArray ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress);
    buffer.Write(ConvertUshort(count).ToBytes(true));
    buffer.WriteCrc16();

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);

    // 返回数据
    return new BitArray(result[3..^2].ToArray());
  }

  /// <inheritdoc />
  public override void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress);

    var data = (ushort)(value ? 0x00FF : 0x0000);

    // 使用小端模式转换数据
    buffer.Write(data.ToBytes());

    buffer.WriteCrc16();

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override void WriteSingleRegister(int unitIdentifier, int startingAddress, ushort value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress);

    // 这里写入时不区分大小端 所以要提前调整好
    buffer.Write(value.ToBytes());

    buffer.WriteCrc16();

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress,
      data.Length + 8);

    // 写寄存器数量
    buffer.Write(ConvertUshort(data.Length / 2).ToBytes(true));

    // 写字节数
    buffer.Write(ConvertByte(data.Length).ToBytes());

    // 写数据
    buffer.Write(data);

    // 写校验
    buffer.WriteCrc16();

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);
  }

  #region 通用方法

  /// <inheritdoc />
  protected override Span<ushort> ReadRegisters(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress,
    int count)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress);
    buffer.Write(ConvertUshort(count).ToBytes(true));
    buffer.WriteCrc16();

    // 1设备地址 1功能码 1数据长度 2n数据 2校验
    var length = 1 + 1 + 1 + count * 2 + 2;
    var result = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);

    // 返回数据
    return result[3..^2].Cast<ushort>();
  }

  /// <inheritdoc />
  protected override ArrayBufferWriter<byte> CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, int initialCapacity = 8)
  {
    var buffer = new ArrayBufferWriter<byte>(initialCapacity);

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier).ToBytes());

    // 写功能码
    buffer.Write(functionCode.ToBytes());

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).ToBytes(true));

    return buffer;
  }

  /// <inheritdoc />
  protected override void VerifyFrame(ReadOnlySpan<byte> data)
  {
    if (data.Length < 5) throw new ModbusException("The response message length is invalid.");
    var crc = Crc16.CalculateCrc16(data[..^2]);

    // 检查校验值是否正确 注意是小端模式
    if (crc != BitConverter.ToUInt16(data[^2..])) throw new ModbusException("CRC16 check error");

    // 检查错误码
    if ((data[1] & 0x80) != 0) ProcessError((ModbusFunctionCode)data[1], (ModbusExceptionCode)data[2]);
  }

  #endregion
}