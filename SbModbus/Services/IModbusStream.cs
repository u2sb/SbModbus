using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Services;

/// <summary>
///   串口
/// </summary>
public interface IModbusStream
{
  /// <summary>
  ///   是否连接
  /// </summary>
  public bool IsConnected { get; protected set; }

  /// <summary>
  ///   读取超时时间
  /// </summary>
  public int ReadTimeout { get; set; }

  /// <summary>
  ///   写入超时时间
  /// </summary>
  public int WriteTimeout { get; set; }

  /// <summary>
  ///   清除读缓存
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask ClearReadBufferAsync(CancellationToken ct);

  /// <summary>
  ///   写入数据
  /// </summary>
  /// <param name="data"></param>
  public void Write(ReadOnlySpan<byte> data);

  /// <summary>
  ///   异步写入数据
  /// </summary>
  /// <param name="data"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

  /// <summary>
  ///   读取数据
  /// </summary>
  /// <param name="span"></param>
  /// <returns></returns>
  public ReadOnlySpan<int> Read(Span<byte> span);


  /// <summary>
  ///   异步读取数据
  /// </summary>
  /// <param name="memory"></param>
  /// <param name="ct"></param>
  /// <returns></returns>
  public ValueTask<int> ReadAsync(Memory<byte> memory, CancellationToken ct);
}