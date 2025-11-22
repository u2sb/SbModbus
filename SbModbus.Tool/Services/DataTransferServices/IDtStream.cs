using System.Buffers;
using System.Text;

namespace SbModbus.Tool.Services.DataTransferServices;

public interface IDtStream : IDisposable
{
  /// <summary>
  ///   数据发送时
  /// </summary>
  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }

  /// <summary>
  ///   数据接收时
  /// </summary>
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }

  /// <summary>
  ///   连接状态变化时
  /// </summary>
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected { get; }

  public bool Connect();

  public void Disconnect();

  public void Write(ReadOnlySpan<byte> data);

  public void Write(string srt, Encoding? encoding = null)
  {
    encoding ??= Encoding.UTF8;
    Write(encoding.GetBytes(srt));
  }

  public ValueTask WriteAsync(ReadOnlyMemory<byte> data);

  public async ValueTask WriteAsync(string srt, Encoding? encoding = null)
  {
    encoding ??= Encoding.UTF8;
    await WriteAsync(encoding.GetBytes(srt));
  }

  public void Write<T>(T target, ReadOnlySpan<byte> data)
  {
    Write(data);
  }

  public void Write<T>(T target, string srt, Encoding? encoding = null)
  {
    Write(srt, encoding);
  }

  public async ValueTask WriteAsync<T>(T target, ReadOnlyMemory<byte> data)
  {
    await WriteAsync(data);
  }

  public async ValueTask WriteAsync<T>(T target, string srt, Encoding? encoding = null)
  {
    await WriteAsync(srt, encoding);
  }
}