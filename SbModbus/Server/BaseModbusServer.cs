using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Server;

/// <inheritdoc />
public class BaseModbusServer : IModbusServer
{
  /// <summary>
  ///   线圈
  /// </summary>
  protected readonly byte[] _coils = new byte[2000 / 8];

  /// <summary>
  ///   离散
  /// </summary>
  protected readonly byte[] _discreteInputs = new byte[2000 / 8];

  /// <summary>
  ///   寄存器
  /// </summary>
  protected readonly byte[] _registers = new byte[65536 * 2];

  /// <summary>
  ///   清除读缓存
  /// </summary>
  public Func<Stream, CancellationToken, Task>? ClearReadBufferAsync;


  /// <summary>
  /// </summary>
  /// <param name="stream"></param>
  /// <param name="unitIdentifier"></param>
  public BaseModbusServer(Stream stream, byte unitIdentifier)
  {
    Stream = stream;
    UnitIdentifier = unitIdentifier;

    ReadTimeout = Stream.ReadTimeout;
    WriteTimeout = Stream.WriteTimeout;

    CheckIsConnected = s => s.CanRead;
    IsConnected = () => CheckIsConnected(Stream);


    _looperTask = Task.Factory.StartNew(
      async () => await Looper(_looperCts.Token),
      _looperCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default
    ).Unwrap();
  }


  /// <summary>
  ///   读超时时间
  /// </summary>
  public int ReadTimeout { get; set; }

  /// <summary>
  ///   写超时时间
  /// </summary>
  public int WriteTimeout { get; set; }

  /// <summary>
  ///   检查是否已连接
  /// </summary>
  public Func<Stream, bool> CheckIsConnected { private get; set; }

  /// <inheritdoc />
  public Stream Stream { get; }

  /// <inheritdoc />
  public Func<bool> IsConnected { get; }

  /// <inheritdoc />
  public BigAndSmallEndianEncodingMode EncodingMode { get; set; }

  /// <inheritdoc />
  public BitSpan Coils => new(_coils);

  /// <inheritdoc />
  public BitSpan DiscreteInputs => new(_discreteInputs);

  /// <inheritdoc />
  public Span<byte> Registers => new(_registers);

  /// <inheritdoc />
  public byte UnitIdentifier { get; }

  /// <inheritdoc />
  public OnWritten? OnCoilsWritten { get; set; }

  /// <inheritdoc />
  public OnWritten? OnRegistersWritten { get; set; }

  /// <inheritdoc />
  public Span<T> GetRegisters<T>() where T : struct
  {
    return Registers.Cast<T>();
  }

  /// <inheritdoc />
  public void Dispose()
  {
    _looperCts.Cancel();
    try
    {
      _looperTask.Wait();
    }
    catch (AggregateException ae)
    {
      ae.Handle(ex => ex is OperationCanceledException);
    }

    _looperCts.Dispose();
    _looperTask.Dispose();
  }

  #region 循环执行

  private readonly CancellationTokenSource _looperCts = new();
  private readonly Task _looperTask;

  private async Task Looper(CancellationToken ct)
  {
    try
    {
      while (true)
      {
        ct.ThrowIfCancellationRequested();
        await Loop(ct).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException)
    {
      // 正常取消时忽略异常
    }
    // ReSharper disable once FunctionNeverReturns
  }

  /// <summary>
  ///   循环执行
  /// </summary>
  /// <returns></returns>
  protected virtual async Task Loop(CancellationToken ct)
  {
    await Task.CompletedTask;
  }

  #endregion
}