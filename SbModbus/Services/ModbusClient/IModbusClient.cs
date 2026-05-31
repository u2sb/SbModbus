using System;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;

namespace SbModbus.Services.ModbusClient;

/// <summary>
///   ModbusClient
/// </summary>
public interface IModbusClient : IDisposable
{
  /// <summary>
  ///   流
  /// </summary>
  public IModbusStream ModbusStream { get; }

  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; }

  /// <summary>
  ///   不同设备通信间隔时间，单位毫秒，0ms 不启用延时
  /// </summary>
  public int DiffSidIntervalTime { get; set; }

  /// <summary>
  ///   接收到消息时触发
  /// </summary>
  public event ModbusClientHandler OnDataReceived;

  /// <summary>
  ///   接收消息时触发（异步）
  ///   只有真正异步处理时才建议使用此事件，以免阻塞接收线程，简单处理请使用同步事件
  /// </summary>
  public event ModbusClientAsyncHandler OnDataReceivedAsync;

  /// <summary>
  ///   发送消息时触发
  /// </summary>
  public event ModbusClientHandler OnDataSent;

  /// <summary>
  ///   发送消息时触发（异步）
  ///   只有真正异步处理时才建议使用此事件，以免阻塞接收线程，简单处理请使用同步事件
  /// </summary>
  public event ModbusClientAsyncHandler OnDataSentAsync;

  /// <summary>
  ///   连接状态改变时触发
  /// </summary>
  public event ModbusStreamStateHandler<bool>? OnConnectStateChanged;

  /// <summary>
  ///   读取线圈 FC01
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default);

  /// <summary>
  ///   读取离散输入 FC02
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default);

  /// <summary>
  ///   读取保存寄存器 FC03
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask<Memory<byte>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default);

  /// <summary>
  ///   读取输入寄存器 FC04
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask<Memory<byte>> ReadInputRegistersAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default);

  /// <summary>
  ///   写入单个线圈 FC05
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="value"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value,
    CancellationToken ct = default);

  /// <summary>
  ///   写入单个线圈 FC05
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);

  /// <summary>
  ///   写入单个寄存器 FC06
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  public ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);

  /// <summary>
  ///   写入单个寄存器 FC06
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="value"></param>
  /// <param name="ct"></param>
  public ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ushort value,
    CancellationToken ct = default);

  /// <summary>
  ///   写入单个寄存器 FC06
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="value"></param>
  /// <param name="ct"></param>
  public ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, short value,
    CancellationToken ct = default);


  /// <summary>
  ///   写入多个寄存器 FC16
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  public ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);

  /// <summary>
  ///   写入多个线圈 FC15
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="quantity">线圈数量</param>
  /// <param name="data">线圈位打包字节数据</param>
  /// <param name="ct"></param>
  public ValueTask WriteMultipleCoilsAsync(int unitIdentifier, int startingAddress, int quantity, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);
}
