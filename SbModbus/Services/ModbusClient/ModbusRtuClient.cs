using System;
#if NETSTANDARD2_0
using Sb.Extensions.System.Buffers;
#else
using System.Buffers;
#endif
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusClient;

/// <summary>
///   ModbusRTU Client
/// </summary>
/// <param name="stream"></param>
/// <param name="logger"></param>
public partial class ModbusRtuClient(IModbusStream stream, ILogger<ModbusRtuClient>? logger = null) : BaseModbusClient(stream, logger), IModbusClient
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
  /// <param name="expectedSlaveId">期望的从站地址</param>
  /// <param name="expectedFunctionCode">期望的功能码</param>
  /// <exception cref="SbModbusException"></exception>
  protected void VerifyFrame(Span<byte> data, byte expectedSlaveId, ModbusFunctionCode expectedFunctionCode)
  {
    if (data.Length < 5)
    {
      logger?.LogError("RTU VerifyFrame: response too short ({Length} bytes), expected at least 5", data.Length);
      SbModbusThrow.InvalidResponseLength();
    }

    // 检查从站地址
    if (data[0] != expectedSlaveId)
    {
      logger?.LogError("RTU VerifyFrame: slave address mismatch, expected={Expected}, actual={Actual}", expectedSlaveId, data[0]);
      SbModbusThrow.SlaveAddressMismatch();
    }

    // 检查功能码（异常响应时高位会被置1，取低7位比较）
    if ((data[1] & 0x7F) != (byte)expectedFunctionCode)
    {
      logger?.LogError("RTU VerifyFrame: function code mismatch, expected=0x{Expected:X2}, actual=0x{Actual:X2}", (int)expectedFunctionCode, data[1]);
      SbModbusThrow.FunctionCodeMismatch();
    }

    var crc = Crc16.CalculateCrc16(data[..^2]);

    // 检查校验值是否正确 注意是小端模式
    if (crc != data[^2..].ToUInt16())
    {
      logger?.LogError("RTU VerifyFrame: CRC error, calculated=0x{Calculated:X4}, received=0x{Received:X4}", crc, data[^2..].ToUInt16());
      SbModbusThrow.CrcError();
    }

    // 检查错误码
    if ((data[1] & 0x80) != 0)
    {
      logger?.LogWarning("RTU exception response: slave={SlaveId}, function=0x{FunctionCode:X2}, exceptionCode=0x{ExceptionCode:X2}", data[0], data[1], data[2]);
      ProcessError((ModbusFunctionCode)data[1], (ModbusExceptionCode)data[2]);
    }
  }

  #endregion
}
