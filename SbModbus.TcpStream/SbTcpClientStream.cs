using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CavemanTcp;
using CommunityToolkit.HighPerformance;
using SbModbus.Models;

namespace SbModbus.TcpStream;

/// <summary>
/// </summary>
public class SbTcpClientStream : ModbusStream, IModbusStream
{
  /// <inheritdoc />
  public SbTcpClientStream(CavemanTcpClient tcpClient)
  {
    TcpClient = tcpClient;
  }

  /// <inheritdoc />
  public SbTcpClientStream(string ipPort) : this(new CavemanTcpClient(ipPort))
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string serverIpOrHostname, int port, bool ssl) : this(
    new CavemanTcpClient(serverIpOrHostname, port, ssl))
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string serverIpOrHostname, int port, X509Certificate2? certificate = null) : this(
    new CavemanTcpClient(serverIpOrHostname, port, certificate))
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string ipPort, bool ssl, string pfxCertFilename, string pfxPassword) : this(
    new CavemanTcpClient(ipPort, ssl, pfxCertFilename, pfxPassword))
  {
  }

  /// <inheritdoc />
  public SbTcpClientStream(string serverIpOrHostname, int port, bool ssl, string pfxCertFilename, string pfxPassword) :
    this(new CavemanTcpClient(serverIpOrHostname, port, ssl, pfxCertFilename, pfxPassword))
  {
  }

  /// <summary>
  ///   TcpClient
  /// </summary>
  public CavemanTcpClient TcpClient { get; }

  /// <summary>
  ///   连接超时 秒
  /// </summary>
  public int ConnectTimeout { get; set; } = 2;

  /// <inheritdoc />
  public override void Dispose()
  {
    Disconnect();
    TcpClient.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <inheritdoc />
  public override Stream? BaseStream { get; protected set; }

  /// <inheritdoc />
  public override bool IsConnected => TcpClient.IsConnected;

  /// <inheritdoc />
  public override int ReadTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override int WriteTimeout { get; set; } = 2000;

  /// <inheritdoc />
  public override bool Connect()
  {
    if (IsConnected) return IsConnected;

    TcpClient.Connect(ConnectTimeout);

    if (IsConnected)
    {
      BaseStream = TcpClient.GetStream();
    }

    return IsConnected;
  }

  /// <inheritdoc />
  public override bool Disconnect()
  {
    if (!TcpClient.IsConnected) return !IsConnected;

    TcpClient.Disconnect();
    BaseStream = null;

    return !IsConnected;
  }

  /// <inheritdoc />
  public override async ValueTask ClearReadBufferAsync(CancellationToken ct = default)
  {
    if (BaseStream is not null && BaseStream.CanRead)
      while (BaseStream.ReadByte() >= 0)
      {
      }

    await Task.CompletedTask;
  }

  /// <inheritdoc />
  public override void Write(ReadOnlySpan<byte> buffer)
  {
    if (IsConnected)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      OnWrite?.Invoke(buffer, this);
#else
      OnWrite?.Invoke(buffer.ToArray(), this);
#endif
      TcpClient.SendWithTimeout(WriteTimeout, buffer.ToArray());
    }
  }

  /// <inheritdoc />
  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected)
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      OnWrite?.Invoke(buffer.Span, this);
#else
      OnWrite?.Invoke(buffer.ToArray(), this);
#endif
      using var ms = buffer.AsStream();
      await TcpClient.SendWithTimeoutAsync(WriteTimeout, buffer.Length, ms, ct);
    }
  }

  /// <inheritdoc />
  public override int Read(Span<byte> buffer)
  {
    if (IsConnected)
    {
      var result = TcpClient.ReadWithTimeout(ReadTimeout, buffer.Length);
      if (result.Status != ReadResultStatus.Success) return 0;

      if (result is not { BytesRead: > 0, DataStream.CanRead: true }) return 0;

#if NET8_0_OR_GREATER
        result.DataStream.ReadExactly(buffer);
#else
      _ = result.DataStream.Read(buffer);
#endif

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      OnRead?.Invoke(buffer, this);
#else
      OnRead?.Invoke(buffer.ToArray(), this);
#endif
      return (int)result.BytesRead;
    }


    return 0;
  }

  /// <inheritdoc />
  public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    if (IsConnected)
    {
      var result = await TcpClient.ReadWithTimeoutAsync(ReadTimeout, buffer.Length, ct);
      if (result.Status != ReadResultStatus.Success) return 0;

      if (result is not { BytesRead: > 0, DataStream.CanRead: true }) return 0;

#if NET8_0_OR_GREATER
        await result.DataStream.ReadExactlyAsync(buffer, ct);
#else
      _ = await result.DataStream.ReadAsync(buffer, ct);
#endif

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
      OnRead?.Invoke(buffer.Span, this);
#else
      OnRead?.Invoke(buffer.ToArray(), this);
#endif
      return (int)result.BytesRead;
    }

    return 0;
  }
}