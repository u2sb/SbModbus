#if NETSTANDARD2_0
using Sb.Extensions.System.Buffers;
#else
using System.Buffers;
#endif
using System;
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Models;

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
    buffer.Write(TransactionsId);

    // 协议标识符号
    buffer.Write("\0\0"u8);

    // 长度 0占位
    buffer.Write("\0\0"u8);

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier));

    // 写功能码
    buffer.Write(functionCode);

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).WithEndianness(true));

    extendFrame?.Invoke(buffer);

    if (!data.IsEmpty) buffer.Write(data);

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

    // 事务处理标识
    TransactionsId++;
    buffer.Write(TransactionsId);

    // 协议标识符号
    buffer.Write("\0\0"u8);

    // 长度 0占位
    buffer.Write("\0\0"u8);

    // 设备地址
    buffer.Write(ConvertByte(unitIdentifier));

    // 写功能码
    buffer.Write(functionCode);

    // 写寄存器地址
    buffer.Write(ConvertUshort(startingAddress).WithEndianness(true));

    extendFrame?.Invoke(buffer);

    buffer.Write(data);

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

    if (data[..2].ToUInt16() != tid) throw new SbModbusException("The response TransactionsId is invalid.");

    // 检查错误码
    if ((data[7] & 0x80) != 0)
      ProcessError((ModbusFunctionCode)data[7], (ModbusExceptionCode)data[8]);
  }
}
