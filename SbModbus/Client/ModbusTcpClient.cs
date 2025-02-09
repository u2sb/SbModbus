using System;
using System.Buffers;
using System.IO;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Client;

/// <summary>
///   ModbusTcp 类
/// </summary>
/// <param name="stream"></param>
public partial class ModbusTcpClient(Stream stream) : BaseModbusClient(stream), IModbusClient
{
  /// <summary>
  ///   事务Id
  /// </summary>
  protected ushort TransactionsId;

  /// <inheritdoc />
  public override BitSpan ReadCoils(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress, 16);
    buffer.Write(ConvertUshort(count).ToBytes(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    var temp = new Span<byte>(buffer.WrittenSpan.ToArray());
    ((ushort)buffer.WrittenCount).WriteTo(temp[4..6], BigAndSmallEndianEncodingMode.ABCD);
    var result = WriteAndReadWithTimeout(temp, length, ReadTimeout);

    // 返回数据
    return new BitSpan(result[3..].ToArray(), count);
  }

  /// <inheritdoc />
  public override BitSpan ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress, 16);
    buffer.Write(ConvertUshort(count).ToBytes(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    var temp = new Span<byte>(buffer.WrittenSpan.ToArray());
    ((ushort)buffer.WrittenCount).WriteTo(temp[4..6], BigAndSmallEndianEncodingMode.ABCD);
    var result = WriteAndReadWithTimeout(temp, length, ReadTimeout);

    // 返回数据
    return new BitSpan(result[3..].ToArray(), count);
  }


  /// <inheritdoc />
  public override void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress, 16);

    var data = (ushort)(value ? 0x00FF : 0x0000);

    // 使用小端模式转换数据
    buffer.Write(data.ToBytes());

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = new Span<byte>(buffer.WrittenSpan.ToArray());
    ((ushort)buffer.WrittenCount).WriteTo(temp[4..6], BigAndSmallEndianEncodingMode.ABCD);
    _ = WriteAndReadWithTimeout(temp, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override void WriteSingleRegister(int unitIdentifier, int startingAddress, ushort value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress, 16);

    // 这里写入时不区分大小端 所以要提前调整好
    buffer.Write(value.ToBytes());

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = new Span<byte>(buffer.WrittenSpan.ToArray());
    ((ushort)buffer.WrittenCount).WriteTo(temp[4..6], BigAndSmallEndianEncodingMode.ABCD);
    _ = WriteAndReadWithTimeout(temp, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress,
      data.Length + 16);

    // 写寄存器数量
    buffer.Write(ConvertUshort(data.Length / 2).ToBytes(true));

    // 写字节数
    buffer.Write(ConvertByte(data.Length).ToBytes());

    // 写数据
    buffer.Write(data);


    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = new Span<byte>(buffer.WrittenSpan.ToArray());
    ((ushort)buffer.WrittenCount).WriteTo(temp[4..6], BigAndSmallEndianEncodingMode.ABCD);
    _ = WriteAndReadWithTimeout(temp, length, ReadTimeout);
  }

  /// <inheritdoc />
  protected override Span<byte> ReadRegisters(int unitIdentifier, ModbusFunctionCode functionCode, int startingAddress,
    int count)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, 16);
    buffer.Write(ConvertUshort(count).ToBytes(true));

    // 7MBAP 1功能码 1数据长度 2n数据
    var length = 7 + 1 + 1 + count * 2;
    var temp = new Span<byte>(buffer.WrittenSpan.ToArray());
    ((ushort)buffer.WrittenCount).WriteTo(temp[4..6], BigAndSmallEndianEncodingMode.ABCD);
    var result = WriteAndReadWithTimeout(temp, length, ReadTimeout);

    // 返回数据
    return result[3..];
  }

  /// <inheritdoc />
  protected override ArrayBufferWriter<byte> CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress,
    int initialCapacity)
  {
    var buffer = new ArrayBufferWriter<byte>(initialCapacity);

    TransactionsId++;
    // 事务处理标识
    buffer.Write(TransactionsId.ToBytes());

    // 协议标识长度
    buffer.Write("\0\0"u8);

    // 长度 暂时写0
    buffer.Write("\0\0"u8);

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
    if (data.Length < 9) throw new ModbusException("The response message length is invalid.");

    // 检查错误码
    if ((data[7] & 0x80) != 0) ProcessError((ModbusFunctionCode)data[7], (ModbusExceptionCode)data[8]);
  }
}