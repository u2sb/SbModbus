using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Net;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.SerialPortStream;
using SbModbus.Services.ModbusServer;
using SbModbus.TcpStream;
using SbModbus.Tool.Models;
using SbModbus.Tool.Models.Settings;
using SbModbus.Tool.Services.ModbusServices;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.ViewModels.Pages;

[AutoInject]
public partial class ModbusServerPageViewModel : ViewModelBase, IDisposable
{
  private AppSettings? _appSettings;
  private ModbusServerPageSettings? _settings;

  private IModbusServer? _modbusServer;
  private IModbusStreamServer? _streamServer;
  private CancellationTokenSource? _runningCts;

  private readonly List<IDisposable> _sessionSubscriptions = [];

  public ModbusServerPageViewModel()
  {
    if (!IsDesignMode) LoadSettings();
  }

  public void Dispose()
  {
    Stop();
    _modbusServer?.Dispose();
    _streamServer?.Dispose();
  }

  public override void OnLoaded(object? sender, RoutedEventArgs e)
  {
    if (ModbusServerTransport == ModbusServerTransport.SerialPort) RefreshSerialPortNames();
  }

  #region 服务端类型

  /// <summary>
  ///   服务端协议类型
  /// </summary>
  [ObservableProperty]
  public partial ModbusServerProtocol ModbusServerProtocol { get; set; } = ModbusServerProtocol.Tcp;

  /// <summary>
  ///   服务端传输方式
  /// </summary>
  [ObservableProperty]
  public partial ModbusServerTransport ModbusServerTransport { get; set; } = ModbusServerTransport.Tcp;

  /// <summary>
  ///   服务器是否正在运行
  /// </summary>
  [ObservableProperty]
  public partial bool IsRunning { get; set; }

  #endregion

  #region 串口配置

  [ObservableProperty]
  public partial string SerialPortName { get; set; } = string.Empty;

  [ObservableProperty]
  public partial int BaudRate { get; set; }

  partial void OnBaudRateChanged(int value)
  {
    BaudRateString = value.ToString("D");
  }

  [ObservableProperty]
  public partial string? BaudRateString { get; set; } = "9600";

  partial void OnBaudRateStringChanged(string? value)
  {
    if (value is not null && value.TryParseToInt32(out var baudRate)) BaudRate = baudRate;
  }

  [ObservableProperty]
  public partial int DataBits { get; set; } = 8;

  [ObservableProperty]
  public partial Parity Parity { get; set; }

  [ObservableProperty]
  public partial StopBits StopBits { get; set; }

  [ObservableProperty]
  public partial Handshake Handshake { get; set; }

  public ObservableCollection<string> SerialPortNames { get; } = [];

  public int[] DataBitsValues { get; } = [8, 7, 6, 5];

  public string[] ParityValues { get; } = ["None", "Odd", "Even", "Mark", "Space"];

  public string[] StopBitsValues { get; } = ["None", "1", "2", "1.5"];

  public string[] HandshakeValues { get; } = ["None", "XOnXOff", "RequestToSend", "RequestToSendXOnX0ff"];

  #endregion

  #region TCP/UDP 配置

  [ObservableProperty]
  public partial string LocalIp { get; set; } = "0.0.0.0";

  [ObservableProperty]
  public partial string LocalPort { get; set; } = "502";

  [ObservableProperty]
  public partial string TargetIp { get; set; } = "127.0.0.1";

  [ObservableProperty]
  public partial string TargetPort { get; set; } = "502";

  #endregion

  #region 绑定管理

  public ObservableCollection<ModbusBindingItem> BindingItems { get; } = [];

  [RelayCommand]
  private void AddBinding()
  {
    var item = new ModbusBindingItem(() => _modbusServer);
    item.DeleteRequested += it => BindingItems.Remove(it);
    BindingItems.Add(item);
  }

  #endregion

  #region 命令

  [RelayCommand]
  private async Task StartAsync()
  {
    if (IsRunning) return;

    // 先清理旧的
    Stop();

    try
    {
      switch (ModbusServerProtocol, ModbusServerTransport)
      {
        case (ModbusServerProtocol.Rtu, ModbusServerTransport.SerialPort):
          CreateRtuSerialServer();
          break;
        case (ModbusServerProtocol.Rtu, ModbusServerTransport.Tcp):
          CreateRtuTcpServer();
          break;
        case (ModbusServerProtocol.Rtu, ModbusServerTransport.Udp):
          CreateRtuUdpServer();
          break;
        case (ModbusServerProtocol.Rtu, ModbusServerTransport.TcpClient):
          CreateRtuTcpClientServer();
          break;
        case (ModbusServerProtocol.Rtu, ModbusServerTransport.UdpClient):
          CreateRtuUdpClientServer();
          break;
        case (ModbusServerProtocol.Tcp, ModbusServerTransport.Tcp):
          CreateTcpServer();
          break;
        case (ModbusServerProtocol.Tcp, ModbusServerTransport.TcpClient):
          CreateTcpTcpClientServer();
          break;
        default:
          return;
      }

      if (_modbusServer is null || _streamServer is null) return;

      _streamServer.OnSessionConnected += OnSessionConnected;
      _streamServer.OnSessionDisconnected += OnSessionDisconnected;

      _runningCts = new CancellationTokenSource();

      // 启动传输层
      if (!_streamServer.IsListening) _streamServer.Start();

      IsRunning = true;

      // 在后台运行 ModbusServer 的处理循环
      _ = Task.Run(async () =>
      {
        try
        {
          await _modbusServer.StartAsync(_streamServer, _runningCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          // 正常停止
        }
        catch (Exception ex)
        {
          App.Current.Dispatcher.Invoke(() =>
          {
            DsLogs.Add(new DsLog(DateTime.Now,
              System.Text.Encoding.UTF8.GetBytes($"Server error: {ex.Message}"), false));
          });
        }
        finally
        {
          App.Current.Dispatcher.Invoke(() => { IsRunning = false; });
        }
      }, _runningCts.Token);

      await SaveSettingsAsync();
    }
    catch (Exception ex)
    {
      DsLogs.Add(new DsLog(DateTime.Now,
        System.Text.Encoding.UTF8.GetBytes($"Start failed: {ex.Message}"), false));
      Stop();
    }
  }

  [RelayCommand]
  private void Stop()
  {
    if (_runningCts is not null)
    {
      _runningCts.Cancel();
      _runningCts.Dispose();
      _runningCts = null;
    }

    if (_streamServer is not null)
    {
      _streamServer.OnSessionConnected -= OnSessionConnected;
      _streamServer.OnSessionDisconnected -= OnSessionDisconnected;
      if (_streamServer.IsListening) _streamServer.Stop();
    }

    // 取消所有会话订阅
    foreach (var sub in _sessionSubscriptions)
      sub.Dispose();
    _sessionSubscriptions.Clear();

    _modbusServer?.Dispose();
    _modbusServer = null;

    _streamServer?.Dispose();
    _streamServer = null;

    // 清空绑定显示值
    foreach (var item in BindingItems) item.DisplayValue = null;
    IsRunning = false;
  }

  #endregion

  #region 创建服务器

  private void CreateRtuSerialServer()
  {
    var sp = new SerialPort(SerialPortName, BaudRate)
    {
      DataBits = DataBits,
      Parity = Parity,
      StopBits = StopBits,
      Handshake = Handshake
    };

    var stream = new SbSerialPortStream(sp);
    _streamServer = new SbSerialPortStreamServer(stream);
    _modbusServer = new ModbusRtuServer();
  }

  private void CreateRtuTcpServer()
  {
    if (!IPAddress.TryParse(LocalIp, out var localIp) || !LocalPort.TryParseToUInt16(out var localPort))
      return;

    _streamServer = new SbTcpStreamServer(localIp, localPort);
    _modbusServer = new ModbusRtuServer();
  }

  private void CreateRtuUdpServer()
  {
    if (!IPAddress.TryParse(LocalIp, out var localIp) || !LocalPort.TryParseToUInt16(out var localPort))
      return;

    _streamServer = new SbUdpStreamServer(localIp, localPort);
    _modbusServer = new ModbusRtuServer();
  }

  private void CreateTcpServer()
  {
    if (!IPAddress.TryParse(LocalIp, out var localIp) || !LocalPort.TryParseToUInt16(out var localPort))
      return;

    _streamServer = new SbTcpStreamServer(localIp, localPort);
    _modbusServer = new ModbusTcpServer();
  }

  private void CreateRtuTcpClientServer()
  {
    if (!IPAddress.TryParse(TargetIp, out var targetIp) || !TargetPort.TryParseToUInt16(out var targetPort))
      return;

    var stream = new SbTcpClientStream(targetIp, targetPort);
    _streamServer = new SbTcpClientStreamServer(stream);
    _modbusServer = new ModbusRtuServer();
  }

  private void CreateRtuUdpClientServer()
  {
    if (!IPAddress.TryParse(TargetIp, out var targetIp) || !TargetPort.TryParseToUInt16(out var targetPort))
      return;

    var stream = new SbUdpClientStream(targetIp, targetPort);
    _streamServer = new SbUdpClientStreamServer(stream);
    _modbusServer = new ModbusRtuServer();
  }

  private void CreateTcpTcpClientServer()
  {
    if (!IPAddress.TryParse(TargetIp, out var targetIp) || !TargetPort.TryParseToUInt16(out var targetPort))
      return;

    var stream = new SbTcpClientStream(targetIp, targetPort);
    _streamServer = new SbTcpClientStreamServer(stream);
    _modbusServer = new ModbusTcpServer();
  }

  #endregion

  #region 会话事件

  private void OnSessionConnected(IModbusStream session)
  {
    // 订阅会话的数据收发事件用于日志
    session.OnDataReceived += OnSessionDataReceived;
    session.OnDataSent += OnSessionDataSent;

    // 保存会话引用于取消订阅
    _sessionSubscriptions.Add(new SessionSubscription(session, this));
  }

  private void OnSessionDisconnected(IModbusStream session)
  {
    session.OnDataReceived -= OnSessionDataReceived;
    session.OnDataSent -= OnSessionDataSent;

    // 移除订阅引用
    for (var i = _sessionSubscriptions.Count - 1; i >= 0; i--)
      if (_sessionSubscriptions[i] is SessionSubscription sub && sub.Stream == session)
      {
        sub.Dispose();
        _sessionSubscriptions.RemoveAt(i);
        break;
      }
  }

  private void OnSessionDataReceived(IModbusStream sender, ReadOnlyMemory<byte> data)
  {
    if (data.IsEmpty) return;

    App.Current.Dispatcher.Invoke(() =>
    {
      DsLogs.Add(new DsLog(DateTime.Now, data.ToArray(), false));
      while (DsLogs.Count > DsLogMaxShow) DsLogs.RemoveAt(0);
    });
  }

  private void OnSessionDataSent(IModbusStream sender, ReadOnlyMemory<byte> data)
  {
    if (data.IsEmpty) return;

    App.Current.Dispatcher.Invoke(() =>
    {
      DsLogs.Add(new DsLog(DateTime.Now, data.ToArray(), true));
      while (DsLogs.Count > DsLogMaxShow) DsLogs.RemoveAt(0);
    });
  }

  /// <summary>
  ///   会话订阅辅助类，用于管理事件取消订阅
  /// </summary>
  private sealed class SessionSubscription : IDisposable
  {
    public IModbusStream Stream { get; }
    private readonly ModbusServerPageViewModel _owner;

    public SessionSubscription(IModbusStream stream, ModbusServerPageViewModel owner)
    {
      Stream = stream;
      _owner = owner;
    }

    public void Dispose()
    {
      Stream.OnDataReceived -= _owner.OnSessionDataReceived;
      Stream.OnDataSent -= _owner.OnSessionDataSent;
    }
  }

  #endregion

  #region 加载和保存设置

  private void RefreshSerialPortNames()
  {
    SerialPortNames.Clear();
    var ports = SerialPort.GetPortNames();
    if (ports is { Length: > 0 })
      foreach (var port in ports)
        SerialPortNames.Add(port);
    else
      SerialPortNames.Add("None");

    if (SerialPortNames.Count > 0 && !SerialPortNames.Contains(SerialPortName))
      SerialPortName = SerialPortNames[0];
  }

  private void LoadSettings()
  {
    _appSettings = Ioc.Default.GetRequiredService<AppSettings>();
    _settings = _appSettings?.ModbusServer;

    if (_appSettings is not null && _settings is not null)
    {
      DsLogMaxShow = _appSettings.DsLogMaxShow;
      ModbusServerProtocol = _settings.ModbusServerProtocol;
      ModbusServerTransport = _settings.ModbusServerTransport;
      SerialPortName = _settings.SerialPortName ?? "COM1";
      BaudRate = _settings.BaudRate;
      DataBits = _settings.DataBits;
      Parity = _settings.Parity;
      StopBits = _settings.StopBits;
      Handshake = _settings.Handshake;
      LocalIp = _settings.LocalIp;
      LocalPort = _settings.LocalPort.ToString("D");
      TargetIp = _settings.TargetIp;
      TargetPort = _settings.TargetPort.ToString("D");
    }
  }

  private async ValueTask SaveSettingsAsync()
  {
    if (_appSettings is not null && _settings is not null)
    {
      _settings.ModbusServerProtocol = ModbusServerProtocol;
      _settings.ModbusServerTransport = ModbusServerTransport;
      _settings.SerialPortName = SerialPortName;
      _settings.BaudRate = BaudRate;
      _settings.DataBits = DataBits;
      _settings.Parity = Parity;
      _settings.StopBits = StopBits;
      _settings.Handshake = Handshake;
      _settings.LocalIp = LocalIp;
      if (LocalPort.TryParseToUInt16(out var localPort)) _settings.LocalPort = localPort;
      _settings.TargetIp = TargetIp;
      if (TargetPort.TryParseToUInt16(out var targetPort)) _settings.TargetPort = targetPort;

      await _appSettings.SaveAsync();
    }
  }

  #endregion

  #region 日志显示框

  /// <summary>
  ///   显示的日志
  /// </summary>
  public ObservableCollection<DsLog> DsLogs { get; } = [];

  [ObservableProperty]
  public partial int DsLogMaxShow { get; set; } = 50;

  [RelayCommand]
  private void CleanOutput()
  {
    DsLogs.Clear();
  }

  #endregion
}
