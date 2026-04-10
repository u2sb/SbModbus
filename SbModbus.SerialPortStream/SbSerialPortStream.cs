using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;
#if NETSTANDARD2_0 || NET462_OR_GREATER
using CommunityToolkit.HighPerformance;
#endif

namespace SbModbus.SerialPortStream;

/// <summary>
///   串口流
/// </summary>
public class SbSerialPortStream : ModbusStream, IModbusStream
{
  /// <inheritdoc />
  public SbSerialPortStream()
  {
    SerialPort = new SerialPort();
  }

  /// <inheritdoc />
  public SbSerialPortStream(SerialPort serialPort)
  {
    SerialPort = serialPort;
  }

  /// <summary>
  ///   串口
  /// </summary>
  public SerialPort SerialPort { get; }

  /// <inheritdoc />
  public override bool IsConnected => SerialPort.IsOpen;

  /// <inheritdoc />
  public override int ReadTimeout
  {
    get => SerialPort.ReadTimeout;
    set => SerialPort.ReadTimeout = value;
  }

  /// <inheritdoc />
  public override int WriteTimeout
  {
    get => SerialPort.WriteTimeout;
    set => SerialPort.WriteTimeout = value;
  }

  /// <inheritdoc />
  public override bool Connect()
  {
    if (IsConnected) return IsConnected;

    SerialPort.Open();

    if (IsConnected)
    {
      ConnectStateChanged(IsConnected);
      StartAutoReceive();
    }

    return IsConnected;
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    StopAutoReceive();
    if (!IsConnected) return !IsConnected;

    SerialPort.Close();

    ConnectStateChanged(IsConnected);

    return !IsConnected;
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    Disconnect();
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  protected override async ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    var b = SerialPort.BytesToRead;
    if (b > 0)
    {
      var temp = new byte[b];
      await ReadAsync(temp, ct);
    }
  }

  /// <inheritdoc />
  protected override void Write(ReadOnlySpan<byte> buffer)
  {
    if (IsConnected) SerialPort.BaseStream.Write(buffer);
  }

  /// <inheritdoc />
  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected) return SerialPort.BaseStream.WriteAsync(buffer, ct);

#if NET6_0_OR_GREATER
    return ValueTask.CompletedTask;
#else
    return default;
#endif
  }

  #region 自动接收

  private CancellationTokenSource? _autoRecCts;

  /// <summary>
  ///   开始自动接收
  /// </summary>
  private void StartAutoReceive()
  {
    StopAutoReceive();
    _autoRecCts = new CancellationTokenSource();

    _ = Task.Run(() => AutoReceiveAsync(_autoRecCts.Token), _autoRecCts.Token);
  }

  /// <summary>
  ///   自动接收任务
  /// </summary>
  /// <param name="ct"></param>
  private async Task AutoReceiveAsync(CancellationToken ct)
  {
    while (!ct.IsCancellationRequested)
    {
      var buffer = new byte[256];
      var memory = buffer.AsMemory();
      if (IsConnected)
        try
        {
          var bytesRead = await SerialPort.BaseStream.ReadAsync(memory, ct);

          // 通信异常
          if (bytesRead == 0) break;

          WriteBuffer(memory.Span);
        }
        catch (OperationCanceledException)
        {
          // 正常取消任务
          break;
        }
        catch (IOException)
        {
          // IO异常
          break;
        }
        catch (Exception)
        {
          // ignored
        }
      else
        await Task.Yield();
    }
  }

  /// <summary>
  ///   停止自动接收
  /// </summary>
  private void StopAutoReceive()
  {
    _autoRecCts?.Cancel();
    _autoRecCts?.Dispose();
    _autoRecCts = null;
  }

  #endregion
}
