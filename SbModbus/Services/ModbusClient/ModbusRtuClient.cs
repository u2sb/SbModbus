using System;
#if NETSTANDARD2_0
using Sb.Extensions.System.Buffers;
#else
using System.Buffers;
#endif
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusClient;

/// <summary>
///   ModbusRTU Client
/// </summary>
/// <param name="stream"></param>
public partial class ModbusRtuClient(IModbusStream stream) : BaseModbusClient(stream), IModbusClient
{
  #region 通用方法

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

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier).WithEndianness());

    // 写功能码
    buffer.Write(functionCode);

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).WithEndianness(true));

    // 写拓展 寄存器数量 字节数等
    extendFrame?.Invoke(buffer);

    // 写数据 寄存器数量等
    if (!data.IsEmpty) buffer.Write(data);

    buffer.WriteCrc16();

    return buffer;
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
    int startingAddress, ushort data, Action<ArrayBufferWriter<byte>>? extendFrame = null)
  {
    var buffer = new ArrayBufferWriter<byte>(256);

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier).WithEndianness());

    // 写功能码
    buffer.Write(functionCode);

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).WithEndianness(true));

    // 写拓展 寄存器数量 字节数等
    extendFrame?.Invoke(buffer);

    // 写数据 寄存器数量等
    buffer.Write(data);

    buffer.WriteCrc16();

    return buffer;
  }


  /// <summary>
  ///   校验数据
  /// </summary>
  /// <param name="data"></param>
  /// <exception cref="SbModbusException"></exception>
  protected void VerifyFrame(Span<byte> data)
  {
    if (data.Length < 5) throw new SbModbusException("The response message length is invalid.");
    var crc = Crc16.CalculateCrc16(data[..^2]);

    // 检查校验值是否正确 注意是小端模式
    if (crc != data[^2..].ToUInt16()) throw new SbModbusException("CRC16 check error");

    // 检查错误码
    if ((data[1] & 0x80) != 0) ProcessError((ModbusFunctionCode)data[1], (ModbusExceptionCode)data[2]);
  }

  #endregion
}
