using System;

namespace SbModbus.Models;

/// <summary>
///   Channel 中传递的已解析 Modbus 请求帧，携带会话引用用于响应路由
/// </summary>
public readonly struct ModbusFrameMessage
{
  /// <summary>
  ///   会话流 — 用于发送响应
  /// </summary>
  public IModbusStream Session { get; init; }

  /// <summary>
  ///   事务 ID（仅 TCP 使用，RTU 为 0）
  /// </summary>
  public ushort Tid { get; init; }

  /// <summary>
  ///   单元标识符 / 从站地址
  /// </summary>
  public byte UnitId { get; init; }

  /// <summary>
  ///   功能码
  /// </summary>
  public ModbusFunctionCode FunctionCode { get; init; }

  /// <summary>
  ///   起始地址（0-based）
  /// </summary>
  public ushort StartingAddress { get; init; }

  /// <summary>
  ///   数量
  /// </summary>
  public ushort Quantity { get; init; }

  /// <summary>
  ///   请求数据体
  /// </summary>
  public ReadOnlyMemory<byte> Data { get; init; }
}
