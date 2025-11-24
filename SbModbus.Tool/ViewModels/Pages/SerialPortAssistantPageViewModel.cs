using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.IO.Ports;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Media;
using SbModbus.Tool.Models.Settings;
using SbModbus.Tool.Services.DataTransferServices;
using SbModbus.Tool.Services.RecordServices;
using SbModbus.Tool.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using ObservableCollections;
using R3;
using Sb.Extensions.System;

namespace SbModbus.Tool.ViewModels.Pages;

public partial class SerialPortAssistantPageViewModel : ViewModelBase, IDisposable
{
  /// <summary>
  ///   日志记录器
  /// </summary>
  private readonly DataTransmissionRecord? _dtr;

  private AppSettings? _appSettings;

  /// <summary>
  ///   传输类型
  /// </summary>
  [ObservableProperty] private DataTransferType _dataTransferType = DataTransferType.SerialPort;

  private DisposableBag _disposableBag;

  /// <summary>
  ///   数据传输流
  /// </summary>
  private IDtStream? _dtStream;

  /// <summary>
  ///   连接状态
  /// </summary>
  [ObservableProperty] private bool _isConnected;

  private SerialPortAssistantPageSettings? _settings;

  public SerialPortAssistantPageViewModel()
  {
    if (!IsDesignMode)
    {
      _dtr = Ioc.Default.GetRequiredService<DataTransmissionRecord>();
      LoadSettings();
    }

    SerialPortNames =
      _serialPortNames.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    SerialPortNames.AddTo(ref _disposableBag);

    _dsLogs = new ObservableFixedSizeRingBuffer<DsLog>(DsLogMaxShow);
    DsLogs = _dsLogs.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    DsLogs.AddTo(ref _disposableBag);

    Sessions = _sessions.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    Sessions.AddTo(ref _disposableBag);
  }

  public void Dispose()
  {
    Disconnect();
    _dtStream?.Dispose();
    _disposableBag.Dispose();
    GC.SuppressFinalize(this);
  }

  partial void OnDataTransferTypeChanged(DataTransferType value)
  {
    Disconnect();

    if (value == DataTransferType.SerialPort)
    {
      RefreshSerialPortNames();
    }
  }

  #region 命令

  [RelayCommand]
  private async Task ConnectAsync()
  {
    switch (DataTransferType)
    {
      case DataTransferType.SerialPort:
        CreateSerialPort();
        break;
      case DataTransferType.TcpClient:
        CreateTcpClient();
        break;
      case DataTransferType.TcpServer:
        CreateTcpServer();
        break;
      case DataTransferType.UdpClient:
        CreateUdpClient();
        break;
      case DataTransferType.UdpServer:
        CreateUdpServer();
        break;
    }

    if (_dtStream is not null)
    {
      _dtStream.OnConnectStateChanged += OnConnectStateChanged;
      _dtStream.OnDataWrite += OnDataWrite;
      _dtStream.OnDataReceived += OnDataReceived;
      _dtStream.Connect();
    }

    switch (_dtStream)
    {
      case TcpServerDtStream tsds:
        tsds.OnSessionStateChanged += OnSessionStateChanged;
        break;
      case UdpServerDtStream usds:
        usds.OnSessionStateChanged += OnSessionStateChanged;
        break;
    }


    await SaveSettingsAsync();
  }

  [RelayCommand]
  private void Disconnect()
  {
    if (_dtStream is null)
    {
      return;
    }

    if (_dtStream.IsConnected)
    {
      _dtStream.Disconnect();
    }

    _dtStream.OnConnectStateChanged -= OnConnectStateChanged;
    _dtStream.OnDataWrite -= OnDataWrite;
    _dtStream.OnDataReceived -= OnDataReceived;

    if (_dtStream is TcpServerDtStream tsds)
    {
      _sessions.Clear();
      SelectedSessionIndex = -1;
      tsds.OnSessionStateChanged -= OnSessionStateChanged;
    }

    if (_dtStream is UdpServerDtStream usds)
    {
      _sessions.Clear();
      SelectedSessionIndex = -1;
      usds.OnSessionStateChanged -= OnSessionStateChanged;
    }

    _dtStream.Dispose();
    _dtStream = null;
  }

  #endregion

  #region 串口方式

  /// <summary>
  ///   串口号
  /// </summary>
  [ObservableProperty] private string? _serialPortName;

  /// <summary>
  ///   波特率
  /// </summary>
  [ObservableProperty] private int _baudRate;

  partial void OnBaudRateChanged(int value)
  {
    BaudRateString = value.ToString("D");
  }

  /// <summary>
  ///   波特率
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsInt32))]
  private string? _baudRateString = "9600";

  partial void OnBaudRateStringChanged(string? value)
  {
    if (value is not null && value.TryParseToInt32(out var baudRate))
    {
      BaudRate = baudRate;
    }
  }

  /// <summary>
  ///   数据位
  /// </summary>
  [ObservableProperty] private int _dataBits;

  /// <summary>
  ///   校验位
  /// </summary>
  [ObservableProperty] private Parity _parity;

  /// <summary>
  ///   停止位
  /// </summary>
  [ObservableProperty] private StopBits _stopBits;

  /// <summary>
  ///   流控
  /// </summary>
  [ObservableProperty] private Handshake _handshake;

  /// <summary>
  ///   串口名称列表
  /// </summary>
  private readonly ObservableList<string> _serialPortNames = [];

  /// <summary>
  ///   串口名称列表
  /// </summary>
  public NotifyCollectionChangedSynchronizedViewList<string> SerialPortNames { get; }

  /// <summary>
  ///   校验位列表
  /// </summary>
  public string[] ParityValues { get; } = ["None", "Odd", "Even", "Mark", "Space"];

  /// <summary>
  ///   停止位列表
  /// </summary>
  public string[] StopBitsValues { get; } = ["None", "1", "2", "1.5"];

  /// <summary>
  ///   流控列表
  /// </summary>
  public string[] HandshakeValues { get; } = ["None", "XOnXOff", "RequestToSend", "RequestToSendXOnX0ff"];

  private void CreateSerialPort()
  {
    Disconnect();

    if (_dtStream is not null && _dtStream is not SerialPortDtStream)
    {
      _dtStream.Dispose();
    }

    _dtStream ??= new SerialPortDtStream();

    if (_dtStream is SerialPortDtStream sps)
    {
      var sp = sps.SerialPort;
      sp.PortName = SerialPortName;
      sp.BaudRate = BaudRate;
      sp.DataBits = DataBits;
      sp.Parity = Parity;
      sp.StopBits = StopBits;
      sp.Handshake = Handshake;
    }
  }

  #endregion

  #region Tcp Udp

  /// <summary>
  ///   IP
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsIp))]
  private string _targetIp = "127.0.0.1";

  /// <summary>
  ///   IP
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsIp))]
  private string _localIp = "0.0.0.0";

  /// <summary>
  ///   目标端口
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsUInt16))]
  private string _targetPort = "13567";

  /// <summary>
  ///   本地端口
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsUInt16))]
  private string _localPort = "13567";

  /// <summary>
  ///   客户端列表
  /// </summary>
  private ObservableList<string> _sessions { get; } = [];

  /// <summary>
  ///   客户端列表
  /// </summary>
  public INotifyCollectionChangedSynchronizedViewList<string> Sessions { get; }

  /// <summary>
  ///   选中的客户端
  /// </summary>
  [ObservableProperty] private int _selectedSessionIndex = -1;

  partial void OnSelectedSessionIndexChanged(int value)
  {
    switch (_dtStream)
    {
      case UdpServerDtStream usds:
        usds.SelectedSessionIndex = value;
        break;
      case TcpServerDtStream tsds:
        tsds.SessionIndex = value;
        break;
    }
  }

  private void CreateTcpClient()
  {
    Disconnect();

    if (_dtStream is not null && _dtStream is not TcpClientDtStream)
    {
      _dtStream.Dispose();
    }

    if (IPAddress.TryParse(TargetIp, out var targetIp) && TargetPort.TryParseToUInt16(out var targetPort))
    {
      _dtStream ??= new TcpClientDtStream(targetIp, targetPort);
    }
  }

  private void CreateTcpServer()
  {
    Disconnect();

    if (_dtStream is not null && _dtStream is not TcpServerDtStream)
    {
      _dtStream.Dispose();
    }

    if (IPAddress.TryParse(LocalIp, out var localIp) && LocalPort.TryParseToUInt16(out var localPort))
    {
      _dtStream ??= new TcpServerDtStream(localIp, localPort);
    }
  }

  private void CreateUdpClient()
  {
    Disconnect();
    if (_dtStream is not null && _dtStream is not UdpClientDtStream)
    {
      _dtStream.Dispose();
    }

    if (IPAddress.TryParse(TargetIp, out var targetIp) && TargetPort.TryParseToUInt16(out var targetPort))
    {
      _dtStream ??= new UdpClientDtStream(targetIp, targetPort);
    }
  }

  private void CreateUdpServer()
  {
    Disconnect();
    if (_dtStream is not null && _dtStream is not UdpServerDtStream)
    {
      _dtStream.Dispose();
    }

    if (IPAddress.TryParse(LocalIp, out var localIp) && LocalPort.TryParseToUInt16(out var localPort))
    {
      _dtStream ??= new UdpServerDtStream(localIp, localPort);
    }
  }

  #endregion

  #region 加载和保存配置文件

  public override void OnLoaded(object sender, RoutedEventArgs e)
  {
    RefreshSerialPortNames();
  }

  private void RefreshSerialPortNames()
  {
    _serialPortNames.Clear();
    var ports = SerialPort.GetPortNames();
    if (ports is { Length: > 0 })
    {
      _serialPortNames.AddRange(ports);
    }
  }

  private void LoadSettings()
  {
    _appSettings = Ioc.Default.GetRequiredService<AppSettings>();
    _settings = _appSettings?.SerialPortAssistant;

    if (_appSettings is not null &&
        _settings is not null)
    {
      DsLogMaxShow = _appSettings.DsLogMaxShow;
      DataTransferType = _settings.DataTransferType;
      SerialPortName = _settings.SerialPortName;
      BaudRate = _settings.BaudRate;
      DataBits = _settings.DataBits;
      Parity = _settings.Parity;
      StopBits = _settings.StopBits;
      Handshake = _settings.Handshake;
      TargetIp = _settings.TargetIp;
      LocalIp = _settings.LocalIp;
      TargetPort = _settings.TargetPort.ToString("D");
      LocalPort = _settings.LocalPort.ToString("D");
    }
  }

  private async ValueTask SaveSettingsAsync()
  {
    if (_appSettings is not null && _settings is not null)
    {
      _settings.DataTransferType = DataTransferType;
      _settings.SerialPortName = SerialPortName;
      _settings.BaudRate = BaudRate;
      _settings.DataBits = DataBits;
      _settings.Parity = Parity;
      _settings.StopBits = StopBits;
      _settings.Handshake = Handshake;
      _settings.TargetIp = TargetIp;
      _settings.LocalIp = LocalIp;
      if (TargetPort.TryParseToUInt16(out var targetPort))
      {
        _settings.TargetPort = targetPort;
      }

      if (LocalPort.TryParseToUInt16(out var localPort))
      {
        _settings.LocalPort = localPort;
      }

      await _appSettings.SaveAsync();
    }
  }

  #endregion

  #region 串口委托

  private void OnConnectStateChanged(bool isConnected)
  {
    IsConnected = isConnected;
    if (isConnected)
    {
      _dtr?.Open();
    }
    else
    {
      _dtr?.Close();
    }
  }

  private void OnDataWrite(ReadOnlySpan<byte> data, IDtStream dtStream)
  {
    _dtr?.WriteOutLog(data);
    _dsLogs.AddLast(new DsLog(DateTime.Now, data.ToArray(), true));
  }

  private void OnDataReceived(ReadOnlySpan<byte> data, IDtStream dtStream)
  {
    _dtr?.WriteInLog(data);
    _dsLogs.AddLast(new DsLog(DateTime.Now, data.ToArray(), false));
  }

  private void OnSessionStateChanged(IEnumerable<string> ids)
  {
    _sessions.Clear();
    _sessions.AddRange(ids);
  }

  #endregion

  #region 输入和输出框

  /// <summary>
  ///   输入框
  /// </summary>
  [ObservableProperty] private string _inputString = string.Empty;

  partial void OnInputStringChanged(string value)
  {
    GetInputBytes();
  }

  /// <summary>
  ///   输出的文本
  /// </summary>
  [ObservableProperty] private string _outputString = string.Empty;

  /// <summary>
  ///   需要发送的Hex值
  /// </summary>
  private readonly ArrayBufferWriter<byte> _inputBuffer = new();
  
  private readonly ObservableFixedSizeRingBuffer<DsLog> _dsLogs;

  /// <summary>
  ///   显示的日志
  /// </summary>
  public INotifyCollectionChangedSynchronizedViewList<DsLog> DsLogs { get; }

  [ObservableProperty] private int _dsLogMaxShow = 50;

  /// <summary>
  ///   输出日志
  /// </summary>
  public record DsLog(DateTime DateTime, byte[] Content, bool IsOut)
  {
    public string DateTimeString => $"[{DateTime:yyyy-MM-dd HH:mm:ss.fff}]";

    public SolidColorBrush TextColor => new(IsOut ? Colors.Blue : Colors.Green);

    public string HexString
    {
      get
      {
        var len = Content.Length * 3 - 1;
        if (len < 0)
        {
          return string.Empty;
        }

        var temp = len < 512 ? stackalloc char[len] : new char[len];
        DataTransmissionRecordLogExtensions.WriteHexChar(Content, temp);
        return temp.ToString();
      }
    }

    public string Utf8String => Encoding.UTF8.GetString(Content);
  }

  /// <summary>
  ///   使用Hex
  /// </summary>
  [ObservableProperty] private bool _useHex = true;

  partial void OnUseHexChanged(bool value)
  {
    InputMode = UseHex ? "Hex" : "Abc";
    GetInputBytes();
  }

  /// <summary>
  ///   输入模式
  /// </summary>
  [ObservableProperty] private string _inputMode = "Hex";

  /// <summary>
  ///   切换输入模式
  /// </summary>
  [RelayCommand]
  private void ChangeInputMode()
  {
    UseHex = !UseHex;
  }

  /// <summary>
  ///   发送
  /// </summary>
  [RelayCommand]
  private async Task SendAsync()
  {
    if (_inputBuffer.WrittenCount == 0)
    {
      return;
    }

    if (_dtStream is { IsConnected: true })
    {
      await _dtStream.WriteAsync(_inputBuffer.WrittenMemory);
    }
  }

  [RelayCommand]
  private void CleanOutput()
  {
    _dsLogs.Clear();
  }

  private void GetInputBytes()
  {
    _inputBuffer.Clear();
    if (!UseHex)
    {
      var bs = Encoding.UTF8.GetBytes(InputString);
      _inputBuffer.Write(bs);
    }
    else
    {
      var source = InputString.AsSpan();
      var len = source.Length;
      var temp = len < 512 ? stackalloc char[len] : new char[len];

      var cp = 0;

      foreach (var c in source)
      {
        if (c != ' ')
        {
          temp[cp] = c;
          cp++;
        }
      }

      try
      {
        var bs = Convert.FromHexString(temp[..cp]);
        _inputBuffer.Write(bs);
      }
      catch (Exception)
      {
        // ignore
      }
    }


    var bl = _inputBuffer.WrittenCount;

    if (bl > 0)
    {
      var len = bl * 3 - 1;
      if (len < 0)
      {
        OutputString = string.Empty;
        return;
      }

      var temp = len < 512 ? stackalloc char[len] : new char[len];
      DataTransmissionRecordLogExtensions.WriteHexChar(_inputBuffer.WrittenSpan, temp);
      OutputString = temp.ToString();
    }
    else
    {
      OutputString = string.Empty;
    }
  }

  #endregion
}