using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SbModbus.Models;
using SbModbus.Services.ModbusServer;

namespace SbModbus.SerialPortStream;

/// <summary>
///   串口服务器流 — 包装 <see cref="SbSerialPortStream" />，实现 <see cref="IModbusStreamServer" />。
///   串口天然是单会话模型，Sessions 列表始终只有 0 或 1 个元素。
/// </summary>
public class SbSerialPortServerStream : IModbusStreamServer
{
  private readonly SbSerialPortStream _stream;
  private readonly Channel<ModbusFrameMessage> _frameChannel =
    Channel.CreateUnbounded<ModbusFrameMessage>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

  private volatile bool _isDisposed;
  private volatile bool _isListening;

  /// <summary>
  ///   创建串口服务器流
  /// </summary>
  /// <param name="stream">底层串口流</param>
  public SbSerialPortServerStream(SbSerialPortStream stream)
  {
    _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    _stream.OnConnectStateChanged += OnStreamConnectStateChanged;
  }

  /// <inheritdoc />
  public bool IsListening => _isListening;

  /// <inheritdoc />
  public int SessionCount => _stream.IsConnected ? 1 : 0;

  /// <inheritdoc />
  public ChannelReader<ModbusFrameMessage> FrameReader => _frameChannel.Reader;

  /// <inheritdoc />
  public event Action<IModbusStream>? OnSessionConnected;

  /// <inheritdoc />
  public event Action<IModbusStream>? OnSessionDisconnected;

  /// <inheritdoc />
  public void Start()
  {
    if (_isDisposed) throw new SbModbusException("Serial port server stream has been disposed");
    if (_isListening) return;

    _stream.Connect();
    _isListening = _stream.IsConnected;

    if (_isListening)
    {
      _stream.AutoReceive = true;
      _stream.OnDataReceived += OnStreamDataReceived;
    }
  }

  /// <inheritdoc />
  public void Stop()
  {
    if (!_isListening) return;

    // 先关闭 Channel，让 ProcessLoopAsync 退出
    _frameChannel.Writer.TryComplete();

    _stream.AutoReceive = false;
    _stream.OnDataReceived -= OnStreamDataReceived;

    _stream.Disconnect();
    _isListening = false;
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    Stop();
    _stream.OnConnectStateChanged -= OnStreamConnectStateChanged;
    _stream.Dispose();

    _frameChannel.Writer.TryComplete();

    OnSessionConnected = null;
    OnSessionDisconnected = null;

    GC.SuppressFinalize(this);
  }

  /// <summary>
  ///   获取底层串口流（用于直接访问串口配置）
  /// </summary>
  public SbSerialPortStream Stream => _stream;

  #region 内部实现

  private void OnStreamDataReceived(IModbusStream sender, ReadOnlyMemory<byte> data)
  {
    using var lb = _stream.GetLockedBuffer();
    if (lb.Buffer.Count == 0) return;
    var remaining = lb.Buffer.WrittenSpan;
    var len = remaining.Length;
    while (FrameParser.TryParseRtu(_stream, ref remaining, out var message))
    {
      _frameChannel.Writer.TryWrite(message);
    }

    var consumed = len - remaining.Length;
    if (consumed > 0)
      lb.Buffer.RemoveFirst(consumed);
  }

  /// <summary>
  ///   串口连接状态变化 → 翻译为会话事件
  /// </summary>
  private void OnStreamConnectStateChanged(IModbusStream sender, bool connected)
  {
    if (_isDisposed) return;

    if (connected && !_isListening)
    {
      _isListening = true;
      try
      {
        OnSessionConnected?.Invoke(_stream);
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "OnSessionConnected callback threw an exception");
      }
    }
    else if (!connected && _isListening)
    {
      _isListening = false;
      try
      {
        OnSessionDisconnected?.Invoke(_stream);
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "OnSessionDisconnected callback threw an exception");
      }
    }
  }

  #endregion
}
