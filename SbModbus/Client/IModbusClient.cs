using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using SbModbus.Models;

namespace SbModbus.Client;

/// <summary>
///   ModbusClient
/// </summary>
public interface IModbusClient
{
  /// <summary>
  ///   流
  /// </summary>
  public Stream Stream { get; }

  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; }

  /// <summary>
  ///   编码方式 未启用
  /// </summary>
  public BigAndSmallEndianEncodingMode EncodingMode { get; set; }

  /// <summary>
  ///   读取线圈 FC01
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public BitArray ReadCoils(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取线圈 FC01
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public ValueTask<BitArray> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取离散输入 FC02
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public BitArray ReadDiscreteInputs(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取离散输入 FC02
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public ValueTask<BitArray> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取保存寄存器 FC03
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public Span<ushort> ReadHoldingRegisters(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取保存寄存器 FC03
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public ValueTask<Memory<ushort>> ReadHoldingRegistersAsync(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取输入寄存器 FC04
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public Span<ushort> ReadInputRegisters(int unitIdentifier, int startingAddress, int count);

  /// <summary>
  ///   读取输入寄存器 FC04
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="count"></param>
  /// <returns></returns>
  public ValueTask<Memory<ushort>> ReadInputRegistersAsync(int unitIdentifier, int startingAddress, int count);

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
  /// <returns></returns>
  public ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value);

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
  public ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ushort value);

  /// <summary>
  ///   写入多个寄存器 FC16
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  public void WriteMultipleRegisters(int unitIdentifier, int startingAddress, ReadOnlySpan<ushort> data);

  /// <summary>
  ///   写入多个寄存器 FC16
  /// </summary>
  /// <param name="unitIdentifier"></param>
  /// <param name="startingAddress"></param>
  /// <param name="data"></param>
  public ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, Memory<ushort> data);

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
  public ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress, Memory<byte> data);
}