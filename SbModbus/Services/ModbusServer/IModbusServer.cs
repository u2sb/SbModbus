using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   Modbus 服务器 — 管理多个传输层会话，共享数据存储和写事件处理器
/// </summary>
public interface IModbusServer : IDisposable
{
  /// <summary>
  ///   所有活跃会话的只读列表
  /// </summary>
  IReadOnlyList<IModbusStream> Sessions { get; }

  /// <summary>
  ///   服务器是否正在运行
  /// </summary>
  bool IsRunning { get; }

  /// <summary>
  ///   线圈
  /// </summary>
  ModbusCoils Coils { get; }

  /// <summary>
  ///   离散输入
  /// </summary>
  ModbusCoils DiscreteInputs { get; }

  /// <summary>
  ///   保持寄存器（读/写）
  /// </summary>
  ModbusRegisters HoldingRegisters { get; }

  /// <summary>
  ///   输入寄存器（只读）
  /// </summary>
  ModbusRegisters InputRegisters { get; }

  /// <summary>
  ///   站号列表
  /// </summary>
  SortedSet<byte> UnitIdentifier { get; }

  /// <summary>
  ///   启动服务器
  /// </summary>
  /// <param name="server">传输层服务器</param>
  /// <param name="ct"></param>
  Task StartAsync(IModbusStreamServer server, CancellationToken ct = default);

  /// <summary>
  ///   停止服务器
  /// </summary>
  Task StopAsync(CancellationToken ct = default);

  /// <summary>
  ///   会话连接时触发
  /// </summary>
  event Action<IModbusStream>? OnSessionConnected;

  /// <summary>
  ///   会话断开时触发
  /// </summary>
  event Action<IModbusStream>? OnSessionDisconnected;

  /// <summary>
  ///   注册线圈写事件
  /// </summary>
  void AddHandler(ModbusCoilsWriteHandler handler);

  /// <summary>
  ///   取消线圈写事件
  /// </summary>
  void RemoveHandler(ModbusCoilsWriteHandler handler);

  /// <summary>
  ///   注册寄存器写事件
  /// </summary>
  void AddHandler(ModbusRegistersWriteHandler handler);

  /// <summary>
  ///   取消寄存器写事件
  /// </summary>
  void RemoveHandler(ModbusRegistersWriteHandler handler);
}
