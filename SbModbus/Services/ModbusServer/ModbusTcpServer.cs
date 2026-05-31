using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   Modbus TCP Server — 以太网协议，使用 MBAP 头
/// </summary>
public class ModbusTcpServer : BaseModbusServer
{
  /// <inheritdoc />
  protected override TryParseFrameDelegate FrameParser => SbModbus.Services.ModbusServer.FrameParser.TryParseTcp;

  #region 响应构建

  /// <inheritdoc />
  protected override async ValueTask SendResponseFrameAsync(
    ModbusStream.LockedModbusStream stream, ushort tid, byte unitId,
    ModbusFunctionCode functionCode, ReadOnlyMemory<byte> pduBody, CancellationToken ct)
  {
    // TCP 正常响应: MBAP(tid, 0x0000, length) + unitId(1) + fc(1) + pduBody
    var pduLength = (ushort)(1 + 1 + pduBody.Length); // unitId + fc + pduBody

    using var buffer = new RentedBuffer(300);

    buffer.Write(tid);
    buffer.Write((ushort)0); // protocolId = 0
    pduLength.WriteTo(buffer.GetSpan(2), BigAndSmallEndianEncodingMode.ABCD);
    buffer.Advance(2);
    buffer.Write(unitId);
    buffer.Write(functionCode);
    if (!pduBody.IsEmpty)
      buffer.Write(pduBody.Span);

    await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);

    Logger.Log(LogLevel.Debug,
      $"TCP response sent: tid={tid}, unit={unitId}, FC=0x{(int)functionCode:X2}, length={buffer.WrittenCount}");
  }

  /// <inheritdoc />
  protected override async ValueTask SendErrorResponseFrameAsync(
    ModbusStream.LockedModbusStream stream, ushort tid, byte unitId,
    ModbusFunctionCode functionCode, ModbusExceptionCode exceptionCode, CancellationToken ct)
  {
    // TCP 错误响应: MBAP(tid, 0x0000, length=3) + unitId(1) + (fc|0x80)(1) + exceptionCode(1)
    using var buffer = new RentedBuffer(16);

    buffer.Write(tid);
    buffer.Write((ushort)0);
    ((ushort)3).WriteTo(buffer.GetSpan(2), BigAndSmallEndianEncodingMode.ABCD);
    buffer.Advance(2);
    buffer.Write(unitId);
    buffer.Write((byte)((byte)functionCode | 0x80));
    buffer.Write((byte)exceptionCode);

    await stream.WriteAsync(buffer.WrittenMemory, ct).ConfigureAwait(false);

    Logger.Log(LogLevel.Warning,
      $"TCP error response: tid={tid}, unit={unitId}, FC=0x{(int)functionCode:X2}, EC=0x{(int)exceptionCode:X2}");
  }

  #endregion

}
