using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Models;

/// <summary>
///   串口
/// </summary>
public interface IModbusStream : IDisposable
{
  /// <summary>
  ///   基础流
  /// </summary>
  public Stream? BaseStream { get; }

  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; }

  /// <summary>
  ///   读取超时时间
  /// </summary>
  public int ReadTimeout { get; }

  /// <summary>
  ///   写入超时时间
  /// </summary>
  public int WriteTimeout { get; }

  /// <summary>
  ///   连接
  /// </summary>
  public bool Connect();

  /// <summary>
  ///   断开连接
  /// </summary>
  public bool Disconnect();

  /// <summary>
  ///   清除读缓存
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask ClearReadBufferAsync(CancellationToken ct = default);

  /// <summary>
  ///   写入数据
  /// </summary>
  /// <param name="buffer"></param>
  public void Write(ReadOnlySpan<byte> buffer);

  /// <summary>
  ///   异步写入数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default);

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <returns></returns>
  public int Read(Span<byte> buffer);


  /// <summary>
  ///   异步读取数据
  /// </summary>
  /// <param name="buffer"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default);
}