using System;
using System.Threading;
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Utils;

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
  protected RentedBuffer CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, ReadOnlySpan<byte> data, Action<RentedBuffer>? extendFrame = null)
  {
    var buffer = new RentedBuffer(300);

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
  protected RentedBuffer CreateFrame(int unitIdentifier, ModbusFunctionCode functionCode,
    int startingAddress, ushort data, Action<RentedBuffer>? extendFrame = null)
  {
    var buffer = new RentedBuffer(300);

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
      Logger.Log(LogLevel.Error, $"TCP VerifyFrame: response too short ({data.Length} bytes), expected at least 9");
      SbModbusThrow.InvalidResponseLength();
    }

    if (data[..2].ToUInt16() != tid)
    {
      Logger.Log(LogLevel.Error, $"TCP VerifyFrame: transaction ID mismatch, expected={tid}, actual={data[..2].ToUInt16()}");
      SbModbusThrow.InvalidTransactionsId();
    }

    // 检查协议标识
    if (data[2] != 0 || data[3] != 0)
    {
      Logger.Log(LogLevel.Error,
        $"TCP VerifyFrame: protocol identifier invalid (expected 0x0000, got 0x{data[2]:X2}{data[3]:X2})");
      SbModbusThrow.InvalidProtocolIdentifier();
    }

    // 检查单元标识符
    if (data[6] != expectedUnitId)
    {
      Logger.Log(LogLevel.Error, $"TCP VerifyFrame: unit identifier mismatch, expected={expectedUnitId}, actual={data[6]}");
      SbModbusThrow.UnitIdentifierMismatch();
    }

    // 检查功能码（异常响应时高位会被置1，取低7位比较）
    if ((data[7] & 0x7F) != (byte)expectedFunctionCode)
    {
      Logger.Log(LogLevel.Error,
        $"TCP VerifyFrame: function code mismatch, expected=0x{(int)expectedFunctionCode:X2}, actual=0x{data[7]:X2}");
      SbModbusThrow.FunctionCodeMismatch();
    }

    // 检查错误码
    if ((data[7] & 0x80) != 0)
    {
      Logger.Log(LogLevel.Warning,
        $"TCP exception response: unit={data[6]}, function=0x{data[7]:X2}, exceptionCode=0x{data[8]:X2}");
      ProcessError((ModbusFunctionCode)data[7], (ModbusExceptionCode)data[8]);
    }
  }
}
