using System;
using System.Collections.Generic;
using SbModbus.Models;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   ModbusServer
/// </summary>
public interface IModbusServer : IDisposable
{
  /// <summary>
  ///   流
  /// </summary>
  public IModbusStream? Stream { get; set; }

  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; }

  /// <summary>
  ///   线圈
  /// </summary>
  public ModbusCoils Coils { get; }

  /// <summary>
  ///   离散输入
  /// </summary>
  public ModbusCoils DiscreteInputs { get; }

  /// <summary>
  ///   保持寄存器
  ///   读/ 写
  /// </summary>
  public ModbusRegisters HoldingRegisters { get; }

  /// <summary>
  ///   输入寄存器
  ///   读
  /// </summary>
  public ModbusRegisters InputRegisters { get; }

  /// <summary>
  ///   站号列表
  /// </summary>
  public SortedSet<byte> UnitIdentifier { get; }

  /// <summary>
  ///   注册事件
  /// </summary>
  /// <param name="handler"></param>
  public void AddHandler(ModbusCoilsWriteHandler handler);

  /// <summary>
  ///   取消事件
  /// </summary>
  /// <param name="handler"></param>
  public void RemoveHandler(ModbusCoilsWriteHandler handler);

  /// <summary>
  ///   注册事件
  /// </summary>
  /// <param name="handler"></param>
  public void AddHandler(ModbusRegistersWriteHandler handler);

  /// <summary>
  ///   取消事件
  /// </summary>
  /// <param name="handler"></param>
  public void RemoveHandler(ModbusRegistersWriteHandler handler);
}
