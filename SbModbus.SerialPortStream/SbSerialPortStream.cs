using System;
using System.Buffers;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using SbModbus.Models;

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
  public override Stream? BaseStream { get; protected set; }

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
      BaseStream = SerialPort.BaseStream;
      ConnectStateChanged(IsConnected);
    }

    return IsConnected;
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    if (!IsConnected) return !IsConnected;

    SerialPort.Close();
    BaseStream = null;

    ConnectStateChanged(IsConnected);

    return !IsConnected;
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
  protected override int Read(Span<byte> buffer)
  {
    var len = Math.Min(SerialPort.BytesToRead, buffer.Length);
    if (BaseStream is not null && len > 0)
    {
      var b = buffer[..len];

#if NET8_0_OR_GREATER
      BaseStream.ReadExactly(b);
#else
      len = BaseStream.Read(b);
#endif
      DataReceived(b);
    }

    return len;
  }

  /// <inheritdoc />
  protected override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(ReadTimeout);
    var len = Math.Min(SerialPort.BytesToRead, buffer.Length);
    if (BaseStream is not null && len > 0)
    {
      var b = buffer[..len];

#if NET8_0_OR_GREATER
      await BaseStream.ReadExactlyAsync(b, cts.Token);
#else
      len = await BaseStream.ReadAsync(b, cts.Token);
#endif
      DataReceived(b.Span);
    }

    return len;
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    Disconnect();
    StreamLock.Dispose();
    GC.SuppressFinalize(this);
  }
}
