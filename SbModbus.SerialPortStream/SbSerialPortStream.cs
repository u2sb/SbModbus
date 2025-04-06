using System;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Services;

namespace SbModbus.SerialPortStream;

/// <summary>
/// </summary>
public class SbSerialPortStream : RJCP.IO.Ports.SerialPortStream, IModbusStream
{
  /// <inheritdoc />
  public bool IsConnected => IsOpen;

  /// <inheritdoc />
  public bool Connect()
  {
    if (!IsConnected) Open();
    return IsConnected;
  }

  /// <inheritdoc />
  public bool Disconnect()
  {
    if (IsConnected) Close();
    return !IsConnected;
  }

  /// <inheritdoc />
  public async ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    var b = BytesToRead;
    if (b > 0)
    {
      var temp = new byte[b];

      await ReadExactlyAsync(temp.AsMemory(0, b), ct);
    }
  }
}