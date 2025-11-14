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
  public override void Dispose()
  {
    Disconnect();
    GC.SuppressFinalize(this);
  }
}