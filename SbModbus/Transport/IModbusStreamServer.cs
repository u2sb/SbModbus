using System;
using System.Threading.Channels;
using SbModbus.Protocol;

namespace SbModbus.Transport;

/// <summary>
///   Modbus 传输层服务器抽象 — 管理监听和会话生命周期。
///   串口/ TCP/ UDP 各自实现此接口。
///   StreamServer 负责解析帧并按 StationId 路由到已注册的 Channel。
/// </summary>
public interface IModbusStreamServer : IDisposable
{
  /// <summary>
  ///   是否正在监听
  /// </summary>
  public bool IsListening { get; }

  /// <summary>
  ///   当前活跃会话数
  /// </summary>
  public int SessionCount { get; }

  /// <summary>
  ///   帧解析器 — 由 <see cref="SbModbus.Services.ModbusServer.BaseModbusServer" /> 在 <c>StartAsync</c> 时注入。
  ///   <see cref="SbModbus.Services.ModbusServer.ModbusRtuServer" /> 注入 RTU 解析；
  ///   <see cref="SbModbus.Services.ModbusServer.ModbusTcpServer" /> 注入 TCP 解析。
  /// </summary>
  public TryParseFrameDelegate FrameParser { set; }

  /// <summary>
  ///   注册站号 — ModbusServer 调用此方法注册其关心的站号。
  ///   注册后，StreamServer 只将匹配 StationId 的帧写入对应的 ChannelWriter。
  /// </summary>
  /// <param name="stationId">从站地址</param>
  /// <param name="writer">对应 ModbusServer 的帧 Channel Writer</param>
  public void RegisterStation(byte stationId, ChannelWriter<ModbusFrameMessage> writer);

  /// <summary>
  ///   取消注册站号
  /// </summary>
  /// <param name="stationId">从站地址</param>
  public void UnregisterStation(byte stationId);

  /// <summary>
  ///   启动监听
  /// </summary>
  public void Start();

  /// <summary>
  ///   停止监听并断开所有会话
  /// </summary>
  public void Stop();

  /// <summary>
  ///   新会话连接时触发
  /// </summary>
  public event Action<IModbusStream>? OnSessionConnected;

  /// <summary>
  ///   会话断开时触发
  /// </summary>
  public event Action<IModbusStream>? OnSessionDisconnected;
}
