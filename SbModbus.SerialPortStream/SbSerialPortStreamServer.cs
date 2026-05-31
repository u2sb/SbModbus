using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using SbModbus.Models;
using SbModbus.Services.ModbusServer;

namespace SbModbus.SerialPortStream;

/// <summary>
///   串口服务器流 — 包装 <see cref="SbSerialPortStream" />，实现 <see cref="IModbusStreamServer" />。
///   串口天然是单会话模型，Sessions 列表始终只有 0 或 1 个元素。
///   按 StationId 将解析后的帧路由到对应注册的 Channel。
/// </summary>
public class SbSerialPortStreamServer : IModbusStreamServer
{
  private readonly ConcurrentDictionary<byte, ChannelWriter<ModbusFrameMessage>> _stationWriters = new();

  private volatile bool _isDisposed;
  private volatile bool _isListening;

  private TryParseFrameDelegate _frameParser = SbModbus.Services.ModbusServer.FrameParser.TryParseRtu;

  /// <summary>
  ///   创建串口服务器流
  /// </summary>
  /// <param name="stream">底层串口流</param>
  public SbSerialPortStreamServer(SbSerialPortStream stream)
  {
    Stream = stream ?? throw new ArgumentNullException(nameof(stream));
    Stream.OnConnectStateChanged += OnStreamConnectStateChanged;
  }

  /// <summary>
  ///   获取底层串口流（用于直接访问串口配置）
  /// </summary>
  public SbSerialPortStream Stream { get; }

  /// <inheritdoc />
  public bool IsListening => _isListening;

  /// <inheritdoc />
  public int SessionCount => Stream.IsConnected ? 1 : 0;

  /// <inheritdoc />
  public TryParseFrameDelegate FrameParser
  {
    set => _frameParser = value;
  }

  /// <inheritdoc />
  public void RegisterStation(byte stationId, ChannelWriter<ModbusFrameMessage> writer)
  {
    _stationWriters[stationId] = writer;
  }

  /// <inheritdoc />
  public void UnregisterStation(byte stationId)
  {
    _stationWriters.TryRemove(stationId, out _);
  }

  /// <inheritdoc />
  public event Action<IModbusStream>? OnSessionConnected;

  /// <inheritdoc />
  public event Action<IModbusStream>? OnSessionDisconnected;

  /// <inheritdoc />
  public void Start()
  {
    if (_isDisposed) throw new SbModbusException("Serial port server stream has been disposed");
    if (_isListening) return;

    Stream.Connect();
    _isListening = Stream.IsConnected;

    if (_isListening)
    {
      Stream.AutoReceive = true;
      Stream.OnDataReceived += OnStreamDataReceived;
    }
  }

  /// <inheritdoc />
  public void Stop()
  {
    if (!_isListening) return;

    Stream.AutoReceive = false;
    Stream.OnDataReceived -= OnStreamDataReceived;

    Stream.Disconnect();
    _isListening = false;
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_isDisposed) return;
    _isDisposed = true;

    Stop();
    Stream.OnConnectStateChanged -= OnStreamConnectStateChanged;
    Stream.Dispose();

    _stationWriters.Clear();

    OnSessionConnected = null;
    OnSessionDisconnected = null;

    GC.SuppressFinalize(this);
  }

  #region 内部实现

  private void OnStreamDataReceived(IModbusStream sender, ReadOnlyMemory<byte> data)
  {
    using var lb = Stream.GetLockedBuffer();
    if (lb.Buffer.Count == 0) return;
    var remaining = lb.Buffer.WrittenSpan;
    var len = remaining.Length;
    while (_frameParser(Stream, ref remaining, out var message))
    {
      if (_stationWriters.TryGetValue(message.UnitId, out var writer))
        writer.TryWrite(message);
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
        OnSessionConnected?.Invoke(Stream);
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
        OnSessionDisconnected?.Invoke(Stream);
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "OnSessionDisconnected callback threw an exception");
      }
    }
  }

  #endregion
}
