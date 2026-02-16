using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Threading;

namespace SbModbus.Models;

/// <summary>
///   串口
/// </summary>
public interface IModbusStream : IDisposable
{
  /// <summary>
  ///   基础流
  /// </summary>
  public Stream? BaseStream { get; }

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
  ///   异步锁
  /// </summary>
  public AsyncLock StreamLock { get; }

  /// <summary>
  ///   连接
  /// </summary>
  public bool Connect();

  /// <summary>
  ///   断开连接
  /// </summary>
  public bool Disconnect();

  /// <summary>
  ///   接收到消息时触发
  /// </summary>
  public event ModbusStreamHandler OnDataReceived;

  /// <summary>
  ///   接收消息时触发（异步）
  ///   只有真正异步处理时才建议使用此事件，以免阻塞接收线程，简单处理请使用同步事件
  /// </summary>
  public event ModbusStreamAsyncHandler OnDataReceivedAsync;

  /// <summary>
  ///   发送消息时触发
  /// </summary>
  public event ModbusStreamHandler OnDataSent;

  /// <summary>
  ///   发送消息时触发（异步）
  ///   只有真正异步处理时才建议使用此事件，以免阻塞接收线程，简单处理请使用同步事件
  /// </summary>
  public event ModbusStreamAsyncHandler OnDataSentAsync;

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
  public ValueTask<ModbusStream.LockedModbusStream> LockAsync(CancellationToken ct = default);
}
