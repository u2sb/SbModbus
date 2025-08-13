using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;
using CommunityToolkit.HighPerformance;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
/// </summary>
public class SbTcpClientStream : CavemanTcpClient, IModbusStream
{
  /// <inheritdoc />
  public int ReadTimeout { get; set; } = 1000;

  /// <inheritdoc />
  public int WriteTimeout { get; set; } = 1000;

  /// <inheritdoc />
  public bool Connect()
  {
    base.Connect(1000);
    return IsConnected;
  }

  /// <inheritdoc />
  public new bool Disconnect()
  {
    base.Disconnect();
    return !IsConnected;
  }

  /// <inheritdoc />
  public async ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    var stream = GetStream();
    if(stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
    await Task.CompletedTask;
  }

  /// <inheritdoc />
  public void Write(ReadOnlySpan<byte> buffer)
  {
    SendWithTimeout(WriteTimeout, buffer.ToArray());
  }

  /// <inheritdoc />
  public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    await SendWithTimeoutAsync(WriteTimeout, buffer.ToArray(), ct);
  }

  /// <inheritdoc />
  public int Read(Span<byte> buffer)
  {
    var result = ReadWithTimeout(ReadTimeout, buffer.Length);
    if (result.Status == ReadResultStatus.Success)
    {
      result.DataStream.Read(buffer);
    }

    return (int)result.BytesRead;
  }

  /// <inheritdoc />
  public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    var result = await ReadWithTimeoutAsync(ReadTimeout, buffer.Length, ct);
    if (result.Status == ReadResultStatus.Success)
    {
      await result.DataStream.ReadAsync(buffer, cancellationToken: ct);
    }

    return (int)result.BytesRead;
  }


  /// <inheritdoc />
  public SbTcpClientStream(string ipPort) : base(ipPort)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string serverIpOrHostname, int port, bool ssl) : base(serverIpOrHostname, port, ssl)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string serverIpOrHostname, int port, X509Certificate2? certificate = null) : base(serverIpOrHostname, port, certificate)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string ipPort, bool ssl, string pfxCertFilename, string pfxPassword) : base(ipPort, ssl, pfxCertFilename, pfxPassword)
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string serverIpOrHostname, int port, bool ssl, string pfxCertFilename, string pfxPassword) : base(serverIpOrHostname, port, ssl, pfxCertFilename, pfxPassword)
  {
  }
}