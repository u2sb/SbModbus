using System;
using System.IO;
using SbBitConverter.Attributes;
using SbBitConverter.Models;

namespace SbModbus.ModbusServer;

/// <summary>
///   ModbusServer
/// </summary>
public interface IModbusServer : IDisposable
{
  /// <summary>
  ///   流
  /// </summary>
  public Stream Stream { get; }

  /// <summary>
  ///   是否连接
  /// </summary>
  public Func<bool> IsConnected { get; }

  /// <summary>
  ///   编码方式 未启用
  /// </summary>
  public BigAndSmallEndianEncodingMode EncodingMode { get; set; }

  /// <summary>
  ///   线圈
  /// </summary>
  public BitSpan Coils { get; }

  /// <summary>
  ///   离散输入
  /// </summary>
  public BitSpan DiscreteInputs { get; }

  /// <summary>
  ///   寄存器
  /// </summary>
  public Span<byte> Registers { get; }

  /// <summary>
  ///   站号列表
  /// </summary>
  public byte UnitIdentifier { get; }

  /// <summary>
  ///   当线圈被写时
  /// </summary>
  public Action? OnCoilsWritten { get; set; }

  /// <summary>
  ///   当寄存器被写时
  /// </summary>
  public Action? OnRegistersWritten { get; set; }

  /// <summary>
  ///   获取寄存器
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <returns></returns>
  public Span<T> GetRegisters<T>() where T : struct;
}