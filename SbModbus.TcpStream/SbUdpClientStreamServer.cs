using SbModbus.Protocol;
using SbModbus.Transport;

namespace SbModbus.TcpStream;

/// <summary>
///   UDP 客户端服务器流 — 包装 <see cref="SbUdpClientStream" />，实现 <see cref="IModbusStreamServer" />。
///   单连接模型，Sessions 列表始终只有 0 或 1 个元素。
///   按 StationId 将解析后的帧路由到对应注册的 Channel。
/// </summary>
public class SbUdpClientStreamServer : ModbusStreamServer<SbUdpClientStream>
{
  /// <summary>
  ///   创建 UDP 客户端服务器流
  /// </summary>
  /// <param name="stream">底层 UDP 客户端流</param>
  public SbUdpClientStreamServer(SbUdpClientStream stream)
    : base(stream, Services.ModbusServer.FrameParser.TryParseTcp)
  {
  }
}
