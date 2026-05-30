using System;
using System.Threading.Channels;

namespace SbModbus.Models;

/// <summary>
///   Modbus 传输层服务器抽象 — 管理监听和会话生命周期。
///   串口/ TCP/ UDP 各自实现此接口。
///   内部通过 Channel 传递解析后的请求帧给上层协议处理。
/// </summary>
public interface IModbusStreamServer : IDisposable
{
  /// <summary>
  ///   是否正在监听
  /// </summary>
  bool IsListening { get; }

  /// <summary>
  ///   当前活跃会话数
  /// </summary>
  int SessionCount { get; }

  /// <summary>
  ///   已解析的 Modbus 请求帧 Channel（上层从此读取）
  /// </summary>
  ChannelReader<ModbusFrameMessage> FrameReader { get; }

  /// <summary>
  ///   启动监听
  /// </summary>
  void Start();

  /// <summary>
  ///   停止监听并断开所有会话
  /// </summary>
  void Stop();

  /// <summary>
  ///   新会话连接时触发
  /// </summary>
  event Action<IModbusStream>? OnSessionConnected;

  /// <summary>
  ///   会话断开时触发
  /// </summary>
  event Action<IModbusStream>? OnSessionDisconnected;
}
