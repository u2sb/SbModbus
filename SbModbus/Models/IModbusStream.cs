using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Models;

/// <summary>
///   表示当传输层接收到数据时触发的回调
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的数据</param>
public delegate void ModbusStreamDataHandler(IModbusStream sender, ReadOnlyMemory<byte> data);

/// <summary>
///   传输层抽象接口
/// </summary>
public interface IModbusStream : IDisposable, IAsyncDisposable
{
  /// <summary>
  ///   是否已销毁
  /// </summary>
  bool IsDisposed { get; }

  /// <summary>
  ///   是否连接
  /// </summary>
  bool IsConnected { get; }

  /// <summary>
  ///   自动接收模式 — 设为 true 时，收到数据会触发 OnDataReceived 事件
  /// </summary>
  bool AutoReceive { get; set; }

  /// <summary>
  ///   读取超时时间 ms
  /// </summary>
  int ReadTimeout { get; set; }

  /// <summary>
  ///   写入超时时间 ms
  /// </summary>
  int WriteTimeout { get; set; }

  /// <summary>
  ///   连接
  /// </summary>
  bool Connect();

  /// <summary>
  ///   断开连接
  /// </summary>
  bool Disconnect();

  /// <summary>
  ///   异步连接
  /// </summary>
  Task<bool> ConnectAsync(CancellationToken ct = default);

  /// <summary>
  ///   异步断开连接
  /// </summary>
  Task<bool> DisconnectAsync(CancellationToken ct = default);

  /// <summary>
  ///   接收到数据时触发（同步）
  /// </summary>
  event ModbusStreamDataHandler? OnDataReceived;

  /// <summary>
  ///   发送数据时触发（同步）
  /// </summary>
  event ModbusStreamDataHandler? OnDataSent;

  /// <summary>
  ///   连接状态改变时触发
  /// </summary>
  event Action<IModbusStream, bool>? OnConnectStateChanged;

  /// <summary>
  ///   锁
  /// </summary>
  ModbusStream.LockedModbusStream Lock();

  /// <summary>
  ///   异步锁
  /// </summary>
  Task<ModbusStream.LockedModbusStream> LockAsync(CancellationToken ct = default);

  /// <summary>
  ///   获取传输层描述信息，用于日志记录
  /// </summary>
  string GetTransportInfo();
}
