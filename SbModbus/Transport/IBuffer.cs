using System;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System.Buffers.RingBuffers;

namespace SbModbus.Transport;

/// <summary>
///   流式数据缓冲区接口 — 将数据缓冲逻辑从 <see cref="ModbusStream" /> 中解耦。
///   实现者负责线程安全。
/// </summary>
internal interface IStreamBuffer : IDisposable
{
  /// <summary>
  ///   当前缓冲的字节数。
  /// </summary>
  int Count { get; }

  /// <summary>
  ///   是否已释放。
  /// </summary>
  bool IsDisposed { get; }

  /// <summary>
  ///   向缓冲区写入数据，并通知等待中的消费者有数据可用。
  /// </summary>
  void Write(ReadOnlySpan<byte> data);

  /// <summary>
  ///   非阻塞读取。从缓冲区读取最多 <paramref name="destination" />.Length 字节。
  /// </summary>
  /// <returns>实际读取的字节数；0 表示当前无数据。</returns>
  int Read(Span<byte> destination);

  /// <summary>
  ///   异步等待并读取数据。阻塞直到至少 1 字节可用、释放或取消。
  /// </summary>
  /// <returns>实际读取的字节数；只有在释放或取消时才返回 0 或抛异常。</returns>
  /// <exception cref="OperationCanceledException">操作被取消或已释放</exception>
  ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken ct = default);

  /// <summary>
  ///   清空缓冲区并唤醒所有等待中的消费者。
  /// </summary>
  void Clear();

  /// <summary>
  ///   获取带锁保护的缓冲区 Span 视图，用于零拷贝帧解析。
  ///   返回的句柄直接持有底层 <see cref="FixedSizeRingBuffer{T}" /> 的锁，
  ///   <see cref="BufferLock.WrittenSpan" /> 是 <see cref="RingBufferSpan{T}" />。
  ///   调用方在 Dispose 句柄前可安全读取/解析 Span。
  /// </summary>
  BufferLock AcquireLock();
}

/// <summary>
///   带锁保护的缓冲区句柄（readonly ref struct，零分配）。
///   构造时自动上锁，Dispose 时自动解锁。
///   调用方直接使用 <c>using var lb = buffer.AcquireLock();</c> 即可。
/// </summary>
public readonly ref struct BufferLock : IDisposable
{
  /// <summary>
  ///   缓冲区当前内容的 <see cref="RingBufferSpan{T}" /> 视图（持有锁期间有效）。
  /// </summary>
  public readonly RingBufferSpan<byte> WrittenSpan => _ringBuffer.WrittenSpan;

  private readonly FixedSizeRingBuffer<byte> _ringBuffer;
  private readonly Lock _locker;

  internal BufferLock(FixedSizeRingBuffer<byte> ringBuffer, Lock locker)
  {
    _ringBuffer = ringBuffer ?? throw new ArgumentNullException(nameof(ringBuffer));
    _locker = locker ?? throw new ArgumentNullException(nameof(locker));
    if (!_locker.IsHeldByCurrentThread)
    {
      _locker.Enter();
    }
  }

  /// <summary>
  ///   从环形缓冲区前端移除指定数量的字节。
  /// </summary>
  public readonly void RemoveFirst(int count) => _ringBuffer.RemoveFirst(count);

  /// <inheritdoc />
  public void Dispose()
  {
    _locker.Exit();
  }
}
