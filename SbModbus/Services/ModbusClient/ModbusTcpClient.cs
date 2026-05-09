#if NETSTANDARD2_0
using Sb.Extensions.System.Buffers;
#else
using System.Buffers;
#endif
using System;
using System.Threading;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Sb.Extensions.System;
using SbModbus.Models;

namespace SbModbus.Services.ModbusClient;

/// <summary>
///   ModbusTcp 类
/// </summary>
/// <param name="stream"></param>
/// <param name="logger"></param>
public partial class ModbusTcpClient(IModbusStream stream, ILogger<ModbusTcpClient>? logger = null) : BaseModbusClient(stream, logger), IModbusClient
{
  /// <summary>
  ///   事务Id
  /// </summary>
  private int _transactionsId;

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
    var tid = (ushort)Interlocked.Increment(ref _transactionsId);
    buffer.Write(tid);

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
    var tid = (ushort)Interlocked.Increment(ref _transactionsId);
    buffer.Write(tid);

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
  /// <param name="expectedUnitId">期望的单元标识符</param>
  /// <param name="expectedFunctionCode">期望的功能码</param>
  /// <exception cref="SbModbusException"></exception>
  protected void VerifyFrame(Span<byte> data, ushort tid, byte expectedUnitId, ModbusFunctionCode expectedFunctionCode)
  {
    if (data.Length < 9)
    {
      logger?.LogError("TCP VerifyFrame: response too short ({Length} bytes), expected at least 9", data.Length);
      SbModbusThrow.InvalidResponseLength();
    }

    if (data[..2].ToUInt16() != tid)
    {
      logger?.LogError("TCP VerifyFrame: transaction ID mismatch, expected={Expected}, actual={Actual}", tid, data[..2].ToUInt16());
      SbModbusThrow.InvalidTransactionsId();
    }

    // 检查协议标识
    if (data[2] != 0 || data[3] != 0)
    {
      logger?.LogError("TCP VerifyFrame: protocol identifier invalid (expected 0x0000, got 0x{Pi:X2}{Pi2:X2})", data[2], data[3]);
      SbModbusThrow.InvalidProtocolIdentifier();
    }

    // 检查单元标识符
    if (data[6] != expectedUnitId)
    {
      logger?.LogError("TCP VerifyFrame: unit identifier mismatch, expected={Expected}, actual={Actual}", expectedUnitId, data[6]);
      SbModbusThrow.UnitIdentifierMismatch();
    }

    // 检查功能码（异常响应时高位会被置1，取低7位比较）
    if ((data[7] & 0x7F) != (byte)expectedFunctionCode)
    {
      logger?.LogError("TCP VerifyFrame: function code mismatch, expected=0x{Expected:X2}, actual=0x{Actual:X2}", (int)expectedFunctionCode, data[7]);
      SbModbusThrow.FunctionCodeMismatch();
    }

    // 检查错误码
    if ((data[7] & 0x80) != 0)
    {
      logger?.LogWarning("TCP exception response: unit={UnitId}, function=0x{FunctionCode:X2}, exceptionCode=0x{ExceptionCode:X2}", data[6], data[7], data[8]);
      ProcessError((ModbusFunctionCode)data[7], (ModbusExceptionCode)data[8]);
    }
  }
}
