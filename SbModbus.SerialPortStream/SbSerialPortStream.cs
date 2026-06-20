using System;
using System.Buffers;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Utils;
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
      Logger.Log(LogLevel.Information, $"Serial port {SerialPort.PortName} connected");
      ConnectStateChanged(IsConnected);
      StartAutoReceive();
    }
    else
    {
      Logger.Log(LogLevel.Warning, $"Serial port {SerialPort.PortName} failed to open");
    }

    return IsConnected;
  }

  /// <inheritdoc />
  public override Task<bool> ConnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(Connect());
  }

  /// <inheritdoc />
  public override string GetTransportInfo()
  {
    // 格式: COM3:9600-8-N-1
    var parity = SerialPort.Parity switch
    {
      Parity.None => 'N',
      Parity.Odd => 'O',
      Parity.Even => 'E',
      Parity.Mark => 'M',
      Parity.Space => 'S',
      _ => '?'
    };

    var stopBits = SerialPort.StopBits switch
    {
      StopBits.None => "0",
      StopBits.One => "1",
      StopBits.Two => "2",
      StopBits.OnePointFive => "1.5",
      _ => "?"
    };

    return $"COM:{SerialPort.PortName}:{SerialPort.BaudRate}-{SerialPort.DataBits}-{parity}-{stopBits}";
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    StopAutoReceive();
    if (!IsConnected) return !IsConnected;

    SerialPort.Close();
    Logger.Log(LogLevel.Information, $"Serial port {SerialPort.PortName} disconnected");
    ConnectStateChanged(IsConnected);

    return !IsConnected;
  }

  /// <inheritdoc />
  public override Task<bool> DisconnectAsync(CancellationToken ct = default)
  {
    return Task.FromResult(Disconnect());
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    Disconnect();
    SerialPort.Dispose();
    base.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public override async ValueTask DisposeAsync()
  {
    await DisconnectAsync().ConfigureAwait(false);
    SerialPort.Dispose();
    await base.DisposeAsync().ConfigureAwait(false);
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  protected override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (!IsConnected)
    {
      Logger.Warning($"Attempted to write to disconnected serial port");
      throw new SbModbusException("Serial port is not connected");
    }

    try
    {
      await SerialPort.BaseStream.WriteAsync(buffer, ct).ConfigureAwait(false);
    }
    catch (InvalidOperationException ex)
    {
      throw new SbModbusException("Serial port is not connected", ex);
    }
    catch (IOException ex)
    {
      throw new SbModbusIOException("Serial port write failed", ex);
    }
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
        try
        {
          var l = SerialPort.Read(temp, 0, length);
          WriteBuffer(temp.AsSpan(0, l));
        }
        finally
        {
          ArrayPool<byte>.Shared.Return(temp);
        }
      }
    }
    catch (InvalidOperationException)
    {
      // 串口已关闭或设备已断开
      Logger.Warning($"Serial port disconnected (InvalidOperationException in DataReceived)");
      ConnectStateChanged(false);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "Unexpected error in SerialPort DataReceived");
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
