using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   Modbus RTU Server — 串口协议，使用 CRC16 校验
/// </summary>
public class ModbusRtuServer : BaseModbusServer
{
  #region 响应构建

  /// <inheritdoc />
  protected override async ValueTask SendResponseFrameAsync(
    ModbusStream.LockedModbusStream stream, ushort tid, byte unitId,
    ModbusFunctionCode functionCode, ReadOnlyMemory<byte> pduBody, CancellationToken ct)
  {
    // RTU 正常响应: slaveId(1) + fc(1) + pduBody + CRC16(2)
    using var buffer = new RentedBuffer(300);

    buffer.Write(unitId);
    buffer.Write(functionCode);
    if (!pduBody.IsEmpty)
      buffer.Write(pduBody.Span);
    buffer.WriteCrc16();

    await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);

    Logger.Log(LogLevel.Debug,
      $"RTU response sent: unit={unitId}, FC=0x{(int)functionCode:X2}, length={buffer.WrittenCount}");
  }

  /// <inheritdoc />
  protected override async ValueTask SendErrorResponseFrameAsync(
    ModbusStream.LockedModbusStream stream, ushort tid, byte unitId,
    ModbusFunctionCode functionCode, ModbusExceptionCode exceptionCode, CancellationToken ct)
  {
    // RTU 错误响应: slaveId(1) + (fc | 0x80)(1) + exceptionCode(1) + CRC16(2)
    using var buffer = new RentedBuffer(16);

    buffer.Write(unitId);
    buffer.Write((byte)((byte)functionCode | 0x80));
    buffer.Write((byte)exceptionCode);
    buffer.WriteCrc16();

    await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);

    Logger.Log(LogLevel.Warning,
      $"RTU error response: unit={unitId}, FC=0x{(int)functionCode:X2}, EC=0x{(int)exceptionCode:X2}");
  }

  #endregion
}
