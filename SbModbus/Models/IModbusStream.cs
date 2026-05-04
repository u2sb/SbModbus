using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Models;

/// <summary>
///   串口
/// </summary>
public interface IModbusStream : IDisposable
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
  ///   连接状态改变时触发
  /// </summary>
  public event ModbusStreamStateHandler<bool>? OnConnectStateChanged;

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
}
