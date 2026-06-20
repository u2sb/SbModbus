using Sb.Extensions.System.Buffers.RingBuffers;
using SbModbus.Protocol;

namespace SbModbus.Transport;

/// <summary>
///   帧解析委托 — 从 RingBuffer 缓冲区中尝试解析出完整的 Modbus 帧。
///   由 <see cref="SbModbus.Services.ModbusServer.BaseModbusServer" /> 子类决定解析策略（RTU/TCP），
///   通过 <see cref="IModbusStreamServer.FrameParser" /> 注入到 StreamServer。
/// </summary>
/// <param name="session">当前会话流</param>
/// <param name="buffer">RingBuffer 缓冲区引用（解析后自动截断已消费部分）</param>
/// <param name="message">解析出的帧消息</param>
/// <returns>true — 成功解析一帧；false — 数据不足或格式错误</returns>
public delegate bool TryParseFrameDelegate(IModbusStream session, ref RingBufferSpan<byte> buffer,
  out ModbusFrameMessage message);
