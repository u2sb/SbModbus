using System;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Services;

namespace SbModbus.ModbusClient;

/// <summary>
///   ModbusClient
/// </summary>
public interface IModbusClient
{
  /// <summary>
  ///   流
  /// </summary>
  public IModbusStream Stream { get; }

  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; }

  /// <summary>
  ///   读取线圈 FC01
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public Span<byte> ReadCoils(int unitIdentifier, int startingAddress, int count);

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
  /// <returns></returns>
  public Span<byte> ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count);

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
  /// <returns></returns>
  public Span<byte> ReadHoldingRegisters(int unitIdentifier, int startingAddress, int count);

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
  /// <returns></returns>
  public Span<byte> ReadInputRegisters(int unitIdentifier, int startingAddress, int count);

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
  /// <returns></returns>
  public void WriteSingleCoil(int unitIdentifier, int startingAddress, bool value);

  /// <summary>
  ///   写入单个线圈 FC05
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="value"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value, CancellationToken ct = default);

  /// <summary>
  ///   写入单个寄存器 FC06
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  public void WriteSingleRegister(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data);

  /// <summary>
  ///   写入单个寄存器 FC06
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="value"></param>
  public void WriteSingleRegister(int unitIdentifier, int startingAddress, ushort value);

  /// <summary>
  ///   写入单个寄存器 FC06
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="value"></param>
  public void WriteSingleRegister(int unitIdentifier, int startingAddress, short value);

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
  public void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<byte> data);

  /// <summary>
  ///   写入多个寄存器 FC16
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  public ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, ReadOnlyMemory<byte> data,
    CancellationToken ct = default);
}