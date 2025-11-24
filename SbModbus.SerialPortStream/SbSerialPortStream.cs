using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
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
      BaseStream = SerialPort.BaseStream;

    return IsConnected;
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    if (!IsConnected) return !IsConnected;

    SerialPort.Close();
    BaseStream = null;

    return !IsConnected;
  }

  /// <inheritdoc />
  public override async ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    var b = SerialPort.BytesToRead;
    if (b > 0)
    {
      var temp = new byte[b];
      await ReadAsync(temp, ct);
    }
  }

  /// <inheritdoc />
  public override int Read(Span<byte> buffer)
  {
    var len = Math.Min(SerialPort.BytesToRead, buffer.Length);
    if (BaseStream is not null && len > 0)
    {
      var b = buffer[..len];
      
#if NET8_0_OR_GREATER
      BaseStream.ReadExactly(b);
#elif NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      _ = BaseStream.Read(b);
#else
      var temp = new byte[len];
      SerialPort.Read(temp, 0, len);
      temp.CopyTo(b);
#endif
    }
    return len;
  }

  /// <inheritdoc />
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    var cts =  CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(ReadTimeout);
    var len = Math.Min(SerialPort.BytesToRead, buffer.Length);
    if (BaseStream is not null && len > 0)
    {
      var b = buffer[..len];
      
#if NET8_0_OR_GREATER
      await BaseStream.ReadExactlyAsync(b, cts.Token);
#elif NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
     _ = await BaseStream.ReadAsync(b, ct);
#else
      var temp = new byte[len];
      SerialPort.Read(temp, 0, len);
      temp.CopyTo(b);
#endif
    }
    return len;
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    Disconnect();
    GC.SuppressFinalize(this);
  }
}