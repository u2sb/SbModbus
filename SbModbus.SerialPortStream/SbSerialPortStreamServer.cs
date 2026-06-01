using SbModbus.Models;

namespace SbModbus.SerialPortStream;

/// <summary>
///   串口服务器流 — 包装 <see cref="SbSerialPortStream" />，实现 <see cref="IModbusStreamServer" />。
///   串口天然是单会话模型，Sessions 列表始终只有 0 或 1 个元素。
///   按 StationId 将解析后的帧路由到对应注册的 Channel。
/// </summary>
public class SbSerialPortStreamServer : ModbusStreamServer<SbSerialPortStream>
{
  /// <summary>
  ///   创建串口服务器流
  /// </summary>
  /// <param name="stream">底层串口流</param>
  public SbSerialPortStreamServer(SbSerialPortStream stream)
    : base(stream, Services.ModbusServer.FrameParser.TryParseRtu)
  {
  }
}
