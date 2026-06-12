using System;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Buffers.RingBuffers;
using SbModbus.Protocol;
using SbModbus.Services.ModbusClient;
using SbModbus.Transport;

namespace SbModbus;

/// <remarks>
///   此文件中的委托被 <see cref="SbModbus.Services.ModbusClient.IModbusClient" /> 的事件
///   和 <see cref="SbModbus.Models.IModbusStream" /> 的 OnConnectStateChanged 事件共同使用。
///   <see cref="ModbusStreamStateHandler{T}" /> 的 sender 参数固定为 IModbusClient，
///   在用于 IModbusStream 场景时调用者负责传递正确的发送方。
/// </remarks>
/// <summary>
///   表示当通信通道传输数据时触发的回调方法
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的只读数据内存块</param>
public delegate void ModbusClientHandler(IModbusClient sender, ReadOnlyMemory<byte> data);

/// <summary>
///   表示当通信通道传输数据时触发的异步回调方法
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的只读数据内存块</param>
/// <param name="ct"></param>
/// <returns>表示异步操作的任务</returns>
public delegate Task ModbusClientAsyncHandler(IModbusClient sender, ReadOnlyMemory<byte> data, CancellationToken ct);

/// <summary>
///   状态改变时触发的回调方法
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="sender"></param>
/// <param name="state"></param>
public delegate void ModbusStreamStateHandler<in T>(IModbusClient sender, T state);

/// <summary>
///   帧解析委托 — 从 RingBuffer 缓冲区中尝试解析出完整的 Modbus 帧。
///   由 <see cref="SbModbus.Services.ModbusServer.BaseModbusServer" /> 子类决定解析策略（RTU/TCP），
///   通过 <see cref="IModbusStreamServer.FrameParser" /> 注入到 StreamServer。
/// </summary>
/// <param name="session">当前会话流</param>
/// <param name="buffer">RingBuffer 缓冲区引用（解析后自动截断已消费部分）</param>
/// <param name="message">解析出的帧消息</param>
/// <returns>true — 成功解析一帧；false — 数据不足或格式错误</returns>
public delegate bool TryParseFrameDelegate(IModbusStream session,
  ref RingBufferSpan<byte> buffer,
  out ModbusFrameMessage message);
