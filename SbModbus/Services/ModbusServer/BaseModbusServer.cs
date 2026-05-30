using System.Collections.Generic;
using System.Threading;
using SbModbus.Models;

namespace SbModbus.Services.ModbusServer;

/// <inheritdoc />
public abstract class BaseModbusServer : IModbusServer
{
  /// <inheritdoc />
  public IModbusStream? Stream { get; set; }

  /// <inheritdoc />
  public bool IsConnected => Stream?.IsConnected ?? false;

  /// <inheritdoc />
  public ModbusCoils Coils { get; } = new();

  /// <inheritdoc />
  public ModbusCoils DiscreteInputs { get; } = new();

  /// <inheritdoc />
  public ModbusRegisters HoldingRegisters { get; } = new();

  /// <inheritdoc />
  public ModbusRegisters InputRegisters { get; } = new();

  /// <inheritdoc />
  public SortedSet<byte> UnitIdentifier { get; } = [];


  /// <inheritdoc />
  public void Dispose()
  {
  }

  #region 写事件

  /// <summary>
  ///   线圈写事件锁
  /// </summary>
  protected readonly
#if NET10_0_OR_GREATER
    Lock
#else
    object
#endif
    CoilsWriteHandlersLocker = new();

  /// <summary>
  ///   寄存器写事件锁
  /// </summary>
  protected readonly
#if NET10_0_OR_GREATER
    Lock
#else
    object
#endif
    RegistersWriteHandlersLocker = new();

  /// <summary>
  ///   线圈写事件
  /// </summary>
  protected List<ModbusCoilsWriteHandler> CoilsWriteHandlers { get; } = [];

  /// <summary>
  ///   寄存器写事件
  /// </summary>
  protected List<ModbusRegistersWriteHandler> RegistersWriteHandlers { get; } = [];

  /// <inheritdoc />
  public void AddHandler(ModbusCoilsWriteHandler handler)
  {
    lock (CoilsWriteHandlersLocker)
    {
      if (CoilsWriteHandlers.Contains(handler)) return;

      CoilsWriteHandlers.Add(handler);
      CoilsWriteHandlers.Sort((p1, p2) => p1.Start.CompareTo(p2.Start));
    }
  }

  /// <inheritdoc />
  public void RemoveHandler(ModbusCoilsWriteHandler handler)
  {
    lock (CoilsWriteHandlersLocker)
    {
      CoilsWriteHandlers.Remove(handler);
    }
  }

  /// <inheritdoc />
  public void AddHandler(ModbusRegistersWriteHandler handler)
  {
    lock (RegistersWriteHandlersLocker)
    {
      if (RegistersWriteHandlers.Contains(handler)) return;

      RegistersWriteHandlers.Add(handler);
      RegistersWriteHandlers.Sort((p1, p2) => p1.Start.CompareTo(p2.Start));
    }
  }

  /// <inheritdoc />
  public void RemoveHandler(ModbusRegistersWriteHandler handler)
  {
    lock (RegistersWriteHandlersLocker)
    {
      RegistersWriteHandlers.Remove(handler);
    }
  }

  #endregion
}
