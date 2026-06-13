using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using SbModbus.Protocol;
using SbModbus.Utils;

namespace SbModbus.Transport;

/// <summary>
///   Modbus 传输层服务器泛型基类 — 包装任意 <typeparamref name="TStream" />，实现 <see cref="IModbusStreamServer" />。
///   单会话模型，Sessions 列表始终只有 0 或 1 个元素。
///   按 StationId 将解析后的帧路由到对应注册的 Channel。
/// </summary>
/// <typeparam name="TStream">底层传输流类型，必须继承 <see cref="ModbusStream" /></typeparam>
public class ModbusStreamServer<TStream> : IModbusStreamServer
  where TStream : ModbusStream
{
  private readonly ConcurrentDictionary<byte, ChannelWriter<ModbusFrameMessage>> _stationWriters = new();

  private TryParseFrameDelegate _frameParser;

  private volatile bool _isDisposed;
  private volatile bool _isListening;

  /// <summary>
  ///   创建传输层服务器流
  /// </summary>
  /// <param name="stream">底层传输流</param>
  /// <param name="defaultFrameParser">默认帧解析器</param>
  protected ModbusStreamServer(TStream stream, TryParseFrameDelegate defaultFrameParser)
  {
    Stream = stream ?? throw new ArgumentNullException(nameof(stream));
    _frameParser = defaultFrameParser ?? throw new ArgumentNullException(nameof(defaultFrameParser));
    Stream.OnConnectStateChanged += OnStreamConnectStateChanged;
  }

  /// <summary>
  ///   获取底层传输流
  /// </summary>
  public TStream Stream { get; }

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
    if (_isDisposed) throw new SbModbusException($"{typeof(TStream).Name} server stream has been disposed");
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
    if (lb.WrittenSpan.Length == 0) return;
    var remaining = lb.WrittenSpan;
    var len = remaining.Length;
    while (_frameParser(Stream, ref remaining, out var message))
      if (_stationWriters.TryGetValue(message.UnitId, out var writer))
        writer.TryWrite(message);

    var consumed = len - remaining.Length;
    if (consumed > 0)
      lb.RemoveFirst(consumed);
  }

  /// <summary>
  ///   传输层连接状态变化 → 翻译为会话事件
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
