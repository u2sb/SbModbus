using SbModbus.Protocol;
using SbModbus.Transport;

namespace SbModbus.TcpStream;

/// <summary>
///   TCP 客户端服务器流 — 包装 <see cref="SbTcpClientStream" />，实现 <see cref="IModbusStreamServer" />。
///   单连接模型，Sessions 列表始终只有 0 或 1 个元素。
///   按 StationId 将解析后的帧路由到对应注册的 Channel。
/// </summary>
public class SbTcpClientStreamServer : ModbusStreamServer<SbTcpClientStream>
{
  /// <summary>
  ///   创建 TCP 客户端服务器流
  /// </summary>
  /// <param name="stream">底层 TCP 客户端流</param>
  public SbTcpClientStreamServer(SbTcpClientStream stream)
    : base(stream, Services.ModbusServer.FrameParser.TryParseTcp)
  {
  }
}
