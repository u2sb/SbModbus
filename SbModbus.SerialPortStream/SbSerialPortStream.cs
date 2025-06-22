using System;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;

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

#if NET8_0_OR_GREATER
      await ReadExactlyAsync(temp.AsMemory(0, b), ct);
#else
      _ = await ReadAsync(temp.AsMemory(0, b), ct);
#endif
    }
  }
}