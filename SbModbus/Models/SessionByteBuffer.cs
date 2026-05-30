using System;

namespace SbModbus.Models;

/// <summary>
///   轻量字节缓冲区 — 用于累积网络接收数据并解析帧
/// </summary>
public sealed class SessionByteBuffer
{
  private byte[] _data = new byte[256];
  private int _count;

  /// <summary>当前缓冲数据</summary>
  public ReadOnlySpan<byte> Span => _data.AsSpan(0, _count);

  /// <summary>追加数据</summary>
  public void Append(ReadOnlySpan<byte> source)
  {
    var needed = _count + source.Length;
    if (needed > _data.Length)
      Array.Resize(ref _data, Math.Max(_data.Length * 2, needed));
    source.CopyTo(_data.AsSpan(_count));
    _count = needed;
  }

  /// <summary>将剩余未消费数据压缩到缓冲区头部，释放前部空间</summary>
  public void CompactTo(ReadOnlySpan<byte> remaining)
  {
    if (remaining.Length == _count) return;
    if (remaining.Length > 0)
      remaining.CopyTo(_data);
    _count = remaining.Length;
  }
}
