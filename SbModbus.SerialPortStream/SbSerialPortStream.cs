using System;
using System.Buffers;
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
    base.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected) return SerialPort.BaseStream.WriteAsync(buffer, ct);

#if NET6_0_OR_GREATER
    return ValueTask.FromException(new IOException("Serial port is not connected"));
#else
    return new ValueTask(Task.FromException(new IOException("Serial port is not connected")));
#endif
  }

  #region 自动接收

  /// <summary>
  ///   开始自动接收
  /// </summary>
  private void StartAutoReceive()
  {
    StopAutoReceive();
    SerialPort.DataReceived += SerialPortOnDataReceived;
  }

  private void SerialPortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
  {
    try
    {
      var length = SerialPort.BytesToRead;

      if (length > 0)
      {
        var temp = ArrayPool<byte>.Shared.Rent(length);
        SerialPort.Read(temp, 0, length);
        WriteBuffer(temp.AsSpan(0, length));
        ArrayPool<byte>.Shared.Return(temp);
      }
    }
    catch (Exception)
    {
      // ignored
    }
  }

  /// <summary>
  ///   停止自动接收
  /// </summary>
  private void StopAutoReceive()
  {
    SerialPort.DataReceived -= SerialPortOnDataReceived;
  }

  #endregion
}
