using System;
using System.Buffers;
using System.Runtime.InteropServices;
using SbBitConverter.Attributes;
using SbBitConverter.Utils;
using SbModbus.Models;
using BitConverter = SbBitConverter.Utils.BitConverter;

namespace SbModbus.Services.ModbusClient;

/// <summary>
///   ModbusTcp 类
/// </summary>
/// <param name="stream"></param>
public partial class ModbusTcpClient(IModbusStream stream) : BaseModbusClient(stream), IModbusClient
{
  /// <summary>
  ///   事务Id
  /// </summary>
  protected ushort TransactionsId;

  /// <inheritdoc />
  public override Span<byte> ReadCoils(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).ToByteArray(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);

    var result = WriteAndReadWithTimeout(temp, length, ReadTimeout);

    // 返回数据
    return result[9..];
  }


  /// <inheritdoc />
  public override void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort((ushort)(value ? 0x00FF : 0x0000)).ToByteArray(true));

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);

    _ = WriteAndReadWithTimeout(temp, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override Span<byte> ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress,
      ConvertUshort(count).ToByteArray(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);

    var result = WriteAndReadWithTimeout(buffer.WrittenMemory, length, ReadTimeout);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  public override void WriteSingleRegister(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress, data);

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    _ = WriteAndReadWithTimeout(temp, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data)
  {
    var l = data.Length;
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress, data, writer =>
    {
      // 写寄存器数量
      writer.Write(ConvertUshort(l / 2).ToByteArray(true));

      // 写字节数
      writer.Write(ConvertByte(l).ToByteArray());
    });


    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    _ = WriteAndReadWithTimeout(temp, length, ReadTimeout);
  }


  /// <inheritdoc />
  protected override Span<byte> ReadRegisters(int unitIdentifier, ModbusFunctionCode functionCode, int startingAddress,
    int count)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ConvertUshort(count).ToByteArray(true));

    // 7MBAP 1功能码 1数据长度 2n数据
    var length = 7 + 1 + 1 + count * 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    var result = WriteAndReadWithTimeout(temp, length, ReadTimeout);

    // 返回数据
    return result[9..];
  }

  /// <summary>
  ///   创建数据帧
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="functionCode"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  /// <param name="extendFrame"></param>
  /// <returns></returns>
  protected ArrayBufferWriter<byte> CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, ReadOnlySpan<byte> data, Action<ArrayBufferWriter<byte>>? extendFrame = null)
  {
    var buffer = new ArrayBufferWriter<byte>(256);

    // 事务处理标识
    TransactionsId++;
    buffer.Write(TransactionsId.ToByteArray());

    // 协议标识符号
    buffer.Write("\0\0"u8);

    // 长度 0占位
    buffer.Write("\0\0"u8);

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier).ToByteArray());

    // 写功能码
    buffer.Write(functionCode.ToByteArray());

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).ToByteArray(true));

    extendFrame?.Invoke(buffer);

    if (!data.IsEmpty) buffer.Write(data);

    return buffer;
  }

  /// <summary>
  ///   校验数据
  /// </summary>
  /// <param name="data">数据</param>
  /// <param name="tid">事务 Id</param>
  /// <exception cref="SbModbusException"></exception>
  protected void VerifyFrame(Span<byte> data, ushort tid)
  {
    if (data.Length < 9) throw new SbModbusException("The response message length is invalid.");

    if (BitConverter.ToUInt16(data[..2]) != tid) throw new SbModbusException("The response TransactionsId is invalid.");

    // 检查错误码
    if ((data[7] & 0x80) != 0)
      ProcessError((ModbusFunctionCode)data[7], (ModbusExceptionCode)data[8]);
  }
}