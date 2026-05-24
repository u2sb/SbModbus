using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Models;

/// <summary>
///   传输层抽象接口
/// </summary>
public interface IModbusStream : IDisposable, IAsyncDisposable
{
  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; }

  /// <summary>
  ///   读取超时时间 ms
  /// </summary>
  public int ReadTimeout { get; set; }

  /// <summary>
  ///   写入超时时间 ms
  /// </summary>
  public int WriteTimeout { get; set; }

  /// <summary>
  ///   连接
  /// </summary>
  public bool Connect();

  /// <summary>
  ///   断开连接
  /// </summary>
  public bool Disconnect();

  /// <summary>
  ///   异步连接
  /// </summary>
  public Task<bool> ConnectAsync(CancellationToken ct = default);

  /// <summary>
  ///   异步断开连接
  /// </summary>
  public Task<bool> DisconnectAsync(CancellationToken ct = default);

  /// <summary>
  ///   连接状态改变时触发
  /// </summary>
  public Action<bool>? OnConnectStateChanged { get; set; }

  /// <summary>
  ///   锁
  /// </summary>
  /// <returns></returns>
  public ModbusStream.LockedModbusStream Lock();

  /// <summary>
  ///   异步锁
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public Task<ModbusStream.LockedModbusStream> LockAsync(CancellationToken ct = default);

  /// <summary>
  ///   获取传输层描述信息，用于日志记录
  /// </summary>
  /// <returns>格式化的传输层描述字符串</returns>
  public string GetTransportInfo();
}
