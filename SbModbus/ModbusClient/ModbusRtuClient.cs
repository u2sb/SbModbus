using System;
using System.Buffers;
using SbBitConverter.Utils;
using SbModbus.Models;
using SbModbus.Services;

namespace SbModbus.ModbusClient;

/// <summary>
///   ModbusRTU Client
/// </summary>
/// <param name="stream"></param>
public partial class ModbusRtuClient(IModbusStream stream) : BaseModbusClient(stream), IModbusClient
{
  /// <inheritdoc />
  public override Span<byte> ReadCoils(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).ToByteArray(true), null);

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);

    // 返回数据
    return result;
  }


  /// <inheritdoc />
  public override Span<byte> ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).ToByteArray(true), null);

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);

    // 返回数据
    return result;
  }


  /// <inheritdoc />
  public override void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress,
      ((ushort)(value ? 0x00FF : 0x0000)).ToByteArray(), null);

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);
  }


  /// <inheritdoc />
  public override void WriteSingleRegister(int unitIdentifier, int startingAddress, ushort value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress,
      value.ToByteArray(), null);

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);
  }

  /// <summary>
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  public override void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data)
  {
    var l = data.Length;
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress, data,
      writer =>
      {
        // 写寄存器数量
        writer.Write(ConvertUshort(l / 2).ToByteArray(true));

        // 写字节数
        writer.Write(ConvertByte(l).ToByteArray());
      });

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);
  }

  #region 通用方法

  /// <inheritdoc />
  protected override ArrayBufferWriter<byte> CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, ReadOnlySpan<byte> data, Action<ArrayBufferWriter<byte>>? extendFrame)
  {
    var buffer = new ArrayBufferWriter<byte>(256);

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier).ToByteArray());

    // 写功能码
    buffer.Write(functionCode.ToByteArray(true));

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).ToByteArray(true));

    // 写拓展 寄存器数量 字节数等
    extendFrame?.Invoke(buffer);

    // 写数据 寄存器数量等
    if (!data.IsEmpty) buffer.Write(data);

    buffer.WriteCrc16();

    return buffer;
  }


  /// <inheritdoc />
  protected override Span<byte> ReadRegisters(int unitIdentifier, ModbusFunctionCode functionCode, int startingAddress,
    int count)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ConvertUshort(count).ToByteArray(true),
      null);

    // 1设备地址 1功能码 1数据长度 2n数据 2校验
    var length = 1 + 1 + 1 + count * 2 + 2;
    var result = WriteAndReadWithTimeout(buffer.WrittenSpan, length, ReadTimeout);

    // 返回数据
    return result[3..^2];
  }


  /// <inheritdoc />
  protected override void VerifyFrame(Span<byte> data)
  {
    if (data.Length < 5) throw new SbModbusException("The response message length is invalid.");
    var crc = Crc16.CalculateCrc16(data[..^2]);

    // 检查校验值是否正确 注意是小端模式
    if (crc != BitConverter.ToUInt16(data[^2..])) throw new SbModbusException("CRC16 check error");

    // 检查错误码
    if ((data[1] & 0x80) != 0) ProcessError((ModbusFunctionCode)data[1], (ModbusExceptionCode)data[2]);
  }

  #endregion
}