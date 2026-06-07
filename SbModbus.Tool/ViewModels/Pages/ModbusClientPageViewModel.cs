using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO.Ports;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Interactivity;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using R3;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.SerialPortStream;
using SbModbus.Services.ModbusClient;
using SbModbus.TcpStream;
using SbModbus.Tool.Models;
using SbModbus.Tool.Models.Settings;
using SbModbus.Tool.Services.ModbusServices;
using SbModbus.Tool.Services.RecordServices;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.ViewModels.Pages;

[AutoInject]
public partial class ModbusClientPageViewModel : ViewModelBase, IDisposable
{
  /// <summary>
  ///   日志记录器
  /// </summary>
  private readonly DataTransmissionRecord? _dtr;

  private AppSettings? _appSettings;

  private DisposableBag _disposableBag;

  private IModbusClient? _modbusClient;

  /// <summary>
  ///   请求
  /// </summary>
  private ModbusRequest? _modbusRequest;

  private IModbusStream? _modbusStream;
  private ModbusClientPageSettings? _settings;

  public ModbusClientPageViewModel()
  {
    if (!IsDesignMode)
    {
      _dtr = GetService<DataTransmissionRecord>();
      LoadSettings();
    }
  }

  /// <summary>
  ///   连接状态
  /// </summary>
  [ObservableProperty]
  public partial bool IsConnected { get; set; }

  /// <summary>
  ///   Modbus客户端模式
  /// </summary>
  [ObservableProperty]
  public partial ModbusClientType ModbusClientType { get; set; } = 0;

  public void Dispose()
  {
    _modbusStream?.Dispose();
    _modbusClient?.Dispose();
    _disposableBag.Dispose();
  }

  public override void OnLoaded(object? sender, RoutedEventArgs e)
  {
    RefreshSerialPortNames();
  }

  partial void OnIsConnectedChanged(bool value)
  {
    if (value)
    {
      _dtr?.Open();
      _modbusClient?.OnDataReceived += OnDataReceived;
      _modbusClient?.OnDataSent += OnDataSent;
    }
    else
    {
      _modbusClient?.OnDataReceived -= OnDataReceived;
      _modbusClient?.OnDataSent -= OnDataSent;
      _dtr?.Close();
    }
  }

  #region 串口方式

  /// <summary>
  ///   串口号
  /// </summary>
  [ObservableProperty]
  public partial string SerialPortName { get; set; } = string.Empty;

  /// <summary>
  ///   波特率
  /// </summary>
  [ObservableProperty]
  public partial int BaudRate { get; set; }

  partial void OnBaudRateChanged(int value)
  {
    BaudRateString = value.ToString("D");
  }

  /// <summary>
  ///   波特率
  /// </summary>
  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsInt32))]
  public partial string? BaudRateString { get; set; } = "9600";

  partial void OnBaudRateStringChanged(string? value)
  {
    if (value is not null && value.TryParseToInt32(out var baudRate)) BaudRate = baudRate;
  }

  /// <summary>
  ///   数据位
  /// </summary>
  [ObservableProperty]
  public partial int DataBits { get; set; }

  /// <summary>
  ///   校验位
  /// </summary>
  [ObservableProperty]
  public partial Parity Parity { get; set; }

  /// <summary>
  ///   停止位
  /// </summary>
  [ObservableProperty]
  public partial StopBits StopBits { get; set; }

  /// <summary>
  ///   流控
  /// </summary>
  [ObservableProperty]
  public partial Handshake Handshake { get; set; }

  /// <summary>
  ///   串口名称列表
  /// </summary>
  public ObservableCollection<string> SerialPortNames { get; } = [];

  /// <summary>
  ///   数据位
  /// </summary>
  public int[] DataBitsValues { get; } = [8, 7, 6, 5];

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


  private void CreateModbusVirtualClient()
  {
    Disconnect();
    _modbusStream = new VirtualModbusStream();
    _modbusClient = new ModbusRtuClient(_modbusStream);
  }

  private void CreateModbusRtuClient()
  {
    Disconnect();

    var sp = new SerialPort(SerialPortName, BaudRate)
    {
      DataBits = DataBits,
      Parity = Parity,
      StopBits = StopBits,
      Handshake = Handshake
    };

    _modbusStream = new SbSerialPortStream(sp);
    _modbusClient = new ModbusRtuClient(_modbusStream);
  }

  #endregion

  #region Tcp

  /// <summary>
  ///   IP
  /// </summary>
  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsIp))]
  public partial string TargetIp { get; set; } = "127.0.0.1";

  /// <summary>
  ///   目标端口
  /// </summary>
  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsUInt16))]
  public partial string TargetPort { get; set; } = "502";

  private void CreateModbusTcpClient()
  {
    Disconnect();

    if (IPAddress.TryParse(TargetIp, out var targetIp) && TargetPort.TryParseToUInt16(out var targetPort))
    {
      _modbusStream = new SbTcpClientStream(targetIp, targetPort);
      _modbusClient = new ModbusTcpClient(_modbusStream);
    }
  }

  private void CreateModbusRtuOverTcpClient()
  {
    Disconnect();

    if (IPAddress.TryParse(TargetIp, out var targetIp) && TargetPort.TryParseToUInt16(out var targetPort))
    {
      _modbusStream = new SbTcpClientStream(targetIp, targetPort);
      _modbusClient = new ModbusRtuClient(_modbusStream);
    }
  }

  private void CreateModbusRtuOverUdpClient()
  {
    Disconnect();

    if (IPAddress.TryParse(TargetIp, out var targetIp) && TargetPort.TryParseToUInt16(out var targetPort))
    {
      _modbusStream = new SbUdpClientStream(targetIp, targetPort);
      _modbusClient = new ModbusRtuClient(_modbusStream);
    }
  }

  #endregion

  #region Modbus读写

  /// <summary>
  ///   站号
  /// </summary>
  [ObservableProperty]
  public partial byte StationId { get; set; } = 1;

  /// <summary>
  ///   站号字符串
  /// </summary>
  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsByte))]
  public partial string? StationIdString { get; set; } = "1";

  partial void OnStationIdStringChanged(string? value)
  {
    if (value is not null && value.TryParseToByte(out var sid)) StationId = sid;
  }

  /// <summary>
  ///   编码方式
  /// </summary>
  [ObservableProperty]
  public partial BigAndSmallEndianEncodingMode EncodingMode { get; set; } =
    BigAndSmallEndianEncodingMode.ABCD;

  /// <summary>
  ///   编码方式
  /// </summary>
  public string[] EncodingModes { get; } = ["DCBA", "ABCD", "BADC", "CDAB"];

  /// <summary>
  ///   被选中的写方法
  /// </summary>
  [ObservableProperty]
  public partial int SelectedModbusWriteFunCodeIndex { get; set; } = 2;

  /// <summary>
  ///   选择的方法
  /// </summary>
  private ModbusFunctionCode SelectedModbusWriteFunctionCode => SelectedModbusWriteFunCodeIndex switch
  {
    0 => ModbusFunctionCode.WriteSingleCoil,
    1 => ModbusFunctionCode.WriteSingleRegister,
    2 => ModbusFunctionCode.WriteMultipleRegisters,
    _ => ModbusFunctionCode.Error
  };

  /// <summary>
  ///   被选中的写FunCode
  /// </summary>
  [ObservableProperty]
  public partial int SelectedModbusReadFunCodeIndex { get; set; } = 2;

  /// <summary>
  ///   选择的方法
  /// </summary>
  private ModbusFunctionCode SelectedModbusReadFunctionCode => SelectedModbusReadFunCodeIndex switch
  {
    0 => ModbusFunctionCode.ReadCoils,
    1 => ModbusFunctionCode.ReadDiscreteInputs,
    2 => ModbusFunctionCode.ReadHoldingRegisters,
    3 => ModbusFunctionCode.ReadInputRegisters,
    _ => ModbusFunctionCode.Error
  };

  /// <summary>
  ///   地址
  /// </summary>
  [ObservableProperty]
  public partial ushort ModbusAddress { get; set; } = 1;

  /// <summary>
  ///   地址
  /// </summary>
  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsUInt16))]
  public partial string? ModbusAddressString { get; set; } = "1";

  partial void OnModbusAddressStringChanged(string? value)
  {
    if (value is not null)
    {
      var number = Convert.ToUInt16(ModbusAddressString, ModbusAddressType == 0 ? 10 : 16);
      ModbusAddress = number;
    }
  }

  /// <summary>
  ///   Modbus地址类型 0 表示十进制 1表示十六进制
  /// </summary>
  [ObservableProperty]
  public partial int ModbusAddressType { get; set; }

  partial void OnModbusAddressTypeChanged(int value)
  {
    var number = Convert.ToUInt16(ModbusAddressString, ModbusAddressType == 0 ? 10 : 16);
    ModbusAddress = number;
  }

  /// <summary>
  ///   寄存器数量
  /// </summary>
  [ObservableProperty]
  public partial byte ReadNumber { get; set; } = 1;

  /// <summary>
  ///   寄存器数量
  /// </summary>
  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsByte))]
  public partial string? ReadNumberString { get; set; } = "1";

  partial void OnReadNumberStringChanged(string? value)
  {
    if (value is not null)
    {
      var number = Convert.ToByte(ReadNumberString, ReadNumberType == 0 ? 10 : 16);
      ReadNumber = number;
    }
  }

  /// <summary>
  ///   寄存器数量类型 0 表示十进制 1表示十六进制
  /// </summary>
  [ObservableProperty]
  public partial int ReadNumberType { get; set; }

  partial void OnReadNumberTypeChanged(int value)
  {
    var number = Convert.ToByte(ReadNumberString, ReadNumberType == 0 ? 10 : 16);
    ReadNumber = number;
  }

  /// <summary>
  ///   Modbus值类型
  /// </summary>
  [ObservableProperty]
  public partial ModbusValueType SelectedModbusValueType { get; set; } = ModbusValueType.UShort;

  /// <summary>
  ///   要读取的寄存器数量
  /// </summary>
  public byte ModbusReadCount => SelectedModbusValueType switch
  {
    ModbusValueType.Bit or ModbusValueType.UShort or ModbusValueType.Short => (byte)(1 * ReadNumber),
    ModbusValueType.Int or ModbusValueType.UInt or ModbusValueType.Float => (byte)(2 * ReadNumber),
    ModbusValueType.Long or ModbusValueType.ULong or ModbusValueType.Double => (byte)(4 * ReadNumber),
    _ => 1
  };


  /// <summary>
  ///   数据类型
  /// </summary>
  public string[] ModbusValueTypes { get; } =
  [
    "bit", "short", "ushort", "int", "uint", "long", "ulong", "float", "double"
  ];

  [ObservableProperty]
  [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsDouble))]
  public partial string? ModbusInputValueString { get; set; } = "0";


  /// <summary>
  ///   Modbus输入值类型 0 表示十进制 1表示十六进制
  /// </summary>
  [ObservableProperty]
  public partial int ModbusInputValueType { get; set; }

  /// <summary>
  ///   读
  /// </summary>
  [RelayCommand]
  private async Task ReadAsync(CancellationToken ct)
  {
    if (_modbusClient is not { IsConnected: true }) return;

    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(3));
    var rc = ModbusReadCount;

    try
    {
      _modbusRequest = ModbusRequest.CreateReadRequest(SelectedModbusReadFunctionCode, ModbusAddress, rc, StationId);

      await using var lockedRequest = await _modbusRequest.LockAsync(cts.Token);
      var result = await lockedRequest.SendAsync(_modbusClient, cts.Token);

      if (result.IsSucceed)
        App.Current.Dispatcher.Invoke(() =>
        {
          MsLogs.Add(new ModbusReadLog(DateTime.Now, result.Data, EncodingMode, result.StartingAddress));
          while (MsLogs.Count > DsLogMaxShow) MsLogs.RemoveAt(0);
        });
    }
    catch (Exception)
    {
      // ignore
      // TODO 异常处理逻辑
    }
  }


  /// <summary>
  ///   写
  /// </summary>
  [RelayCommand]
  private async Task WriteAsync(CancellationToken ct)
  {
    if (_modbusClient is not { IsConnected: true }) return;

    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(3));

    try
    {
      var fromBase = ModbusInputValueType == 0 ? 10 : 16;

      switch (SelectedModbusWriteFunctionCode)
      {
        case ModbusFunctionCode.WriteSingleCoil:
          var bv = ModbusInputValueString != "0";
          _modbusRequest = ModbusRequest.CreateWriteSingleCoil(ModbusAddress, bv, StationId);
          break;
        case ModbusFunctionCode.WriteSingleRegister:
          byte[] srv = [];

          if (ModbusInputValueString is not null)
            switch (SelectedModbusValueType)
            {
              case ModbusValueType.Short:
                var sv = Convert.ToInt16(ModbusInputValueString, fromBase);
                srv = sv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.UShort:
                var usv = Convert.ToUInt16(ModbusInputValueString, fromBase);
                srv = usv.ToByteArray(EncodingMode);
                break;
            }

          if (srv.Length == 2)
            _modbusRequest = ModbusRequest.CreateWriteSingleRegister(ModbusAddress, srv, StationId);
          break;
        case ModbusFunctionCode.WriteMultipleRegisters:
          byte[] mrv = [];

          if (ModbusInputValueString is not null)
            switch (SelectedModbusValueType)
            {
              case ModbusValueType.Short:
                var sv = Convert.ToInt16(ModbusInputValueString, fromBase);
                mrv = sv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.UShort:
                var usv = Convert.ToUInt16(ModbusInputValueString, fromBase);
                mrv = usv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.Int:
                var iv = Convert.ToInt32(ModbusInputValueString, fromBase);
                mrv = iv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.UInt:
                var uiv = Convert.ToUInt32(ModbusInputValueString, fromBase);
                mrv = uiv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.Long:
                var lv = Convert.ToInt64(ModbusInputValueString, fromBase);
                mrv = lv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.ULong:
                var ulv = Convert.ToUInt64(ModbusInputValueString, fromBase);
                mrv = ulv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.Float when ModbusInputValueString.TryParseToFloat(out var fv):
                mrv = fv.ToByteArray(EncodingMode);
                break;
              case ModbusValueType.Double when ModbusInputValueString.TryParseToDouble(out var dv):
                mrv = dv.ToByteArray(EncodingMode);
                break;
            }

          if (mrv.Length > 0)
            _modbusRequest = ModbusRequest.CreateWriteMultipleRegisters(ModbusAddress, mrv, StationId);
          break;
      }

      if (_modbusRequest != null)
      {
        await using var lockedRequest = await _modbusRequest.LockAsync(cts.Token);
        await lockedRequest.SendAsync(_modbusClient, cts.Token);
      }
    }
    catch (Exception)
    {
      // ignore
      // TODO 异常处理逻辑
    }
  }

  #endregion

  #region 命令

  [RelayCommand]
  private async Task ConnectAsync()
  {
    switch (ModbusClientType)
    {
      case ModbusClientType.ModbusVirtual:
        CreateModbusVirtualClient();
        break;
      case ModbusClientType.ModbusRTU:
        CreateModbusRtuClient();
        break;
      case ModbusClientType.ModbusTCP:
        CreateModbusTcpClient();
        break;
      case ModbusClientType.ModbusRTUOverTcp:
        CreateModbusRtuOverTcpClient();
        break;
      case ModbusClientType.ModbusRTUOverUdp:
        CreateModbusRtuOverUdpClient();
        break;
    }

    if (_modbusClient is not null)
      _modbusClient.OnConnectStateChanged += (sender, state) => { IsConnected = state; };

    if (_modbusStream is not null)
      try
      {
        _modbusStream.ReadTimeout = 2000;
        _modbusStream.WriteTimeout = 2000;
        _modbusStream.Connect();
      }
      catch (Exception)
      {
        // ignore
      }

    await SaveSettingsAsync();
  }

  [RelayCommand]
  private void Disconnect()
  {
    if (_modbusStream is null) return;

    if (_modbusStream.IsConnected) _modbusStream.Disconnect();

    if (_modbusClient is not null)
    {
      _modbusClient?.Dispose();
      _modbusClient = null;
    }

    _modbusStream.Dispose();
    _modbusStream = null;
  }

  #endregion

  #region 串口委托

  private void OnDataSent(IModbusClient sender, ReadOnlyMemory<byte> data)
  {
    if (data.IsEmpty) return;

    _dtr?.WriteOutLog(data.Span);
    App.Current.Dispatcher.Invoke(() =>
    {
      DsLogs.Add(new DsLog(DateTime.Now, data.ToArray(), true));
      while (DsLogs.Count > DsLogMaxShow) DsLogs.RemoveAt(0);
    });
  }

  private void OnDataReceived(IModbusClient sender, ReadOnlyMemory<byte> data)
  {
    if (data.IsEmpty) return;

    _dtr?.WriteInLog(data.Span);
    App.Current.Dispatcher.Invoke(() =>
    {
      DsLogs.Add(new DsLog(DateTime.Now, data.ToArray(), false));
      while (DsLogs.Count > DsLogMaxShow) DsLogs.RemoveAt(0);
    });
  }

  #endregion

  #region 加载和保存配置文件

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
    _settings = _appSettings?.ModbusClient;


    if (_appSettings is not null && _settings is not null)
    {
      DsLogMaxShow = _appSettings.DsLogMaxShow;
      ModbusClientType = _settings.ModbusClientType;
      SerialPortName = _settings.SerialPortName;
      BaudRate = _settings.BaudRate;
      DataBits = _settings.DataBits;
      Parity = _settings.Parity;
      StopBits = _settings.StopBits;
      Handshake = _settings.Handshake;
      TargetIp = _settings.TargetIp;
      TargetPort = _settings.TargetPort.ToString("D");
      StationIdString = _settings.StationId.ToString("D");
    }
  }

  private async ValueTask SaveSettingsAsync()
  {
    if (_appSettings is not null && _settings is not null)
    {
      _settings.ModbusClientType = ModbusClientType;
      _settings.SerialPortName = SerialPortName;
      _settings.BaudRate = BaudRate;
      _settings.DataBits = DataBits;
      _settings.Parity = Parity;
      _settings.StopBits = StopBits;
      _settings.Handshake = Handshake;
      _settings.TargetIp = TargetIp;
      if (TargetPort.TryParseToUInt16(out var targetPort)) _settings.TargetPort = targetPort;

      _settings.StationId = StationId;

      await _appSettings.SaveAsync();
    }
  }

  #endregion

  #region 日志显示框

  /// <summary>
  ///   显示的日志
  /// </summary>
  public ObservableCollection<DsLog> DsLogs { get; } = [];

  [ObservableProperty] public partial int DsLogMaxShow { get; set; } = 50;

  public ObservableCollection<ModbusReadLog> MsLogs { get; } = [];

  [RelayCommand]
  private void CleanOutput()
  {
    DsLogs.Clear();
    MsLogs.Clear();
  }

  public record ModbusReadLog(
    DateTime DateTime,
    byte[] Content,
    BigAndSmallEndianEncodingMode EncodingMode,
    ushort StartAddress)
  {
    public string DateTimeString
    {
      get
      {
        var now = DateTime.Now;
        return $"[{now:yyyy-MM-dd}]\r\n[{now:HH:mm:ss.fff}]";
      }
    }

    public string[] Address => GetAddress();

    public string[] Cells => GetArray<bool>();

    public string[] Shorts => GetArray<short>();

    public string[] UShorts => GetArray<ushort>();

    public string[] Ints => GetArray<int>();

    public string[] UInts => GetArray<uint>();

    public string[] Longs => GetArray<long>();

    public string[] ULongs => GetArray<ulong>();

    public string[] Floats => GetArray<float>();

    public string[] Doubles => GetArray<double>();

    private string[] GetArray<T>() where T : unmanaged
    {
      var csp = Content.AsSpan();

      if (typeof(T) == typeof(bool))
      {
        var size = 2;
        var ts = new string[Content.Length / size];
        for (var i = 0; i < ts.Length; i++)
        {
          var ci = size * i;
          if (ci >= Content.Length) break;

          var ti = csp.Slice(ci, size);

          var bit = new BitSpan(ti);
          var sb = new StringBuilder();

          for (var j = 0; j < bit.Length; j++)
          {
            var b = bit[j];
            sb.Append(b ? '1' : '0');
            if (j == 7) sb.Append(' ');
          }

          ts[i] = sb.ToString();
        }

        return ts;
      }
      else
      {
        var size = Unsafe.SizeOf<T>();
        var ts = new string[Content.Length / size];

        for (var i = 0; i < ts.Length; i++)
        {
          var ci = size * i;
          if (ci >= Content.Length) break;

          var ti = csp.Slice(ci, size).ToT<T>(EncodingMode);

          ts[i] = typeof(T) switch
          {
            { } t when t == typeof(byte) => $"{ti:X2}/{ti:D3}",
            { } t when t == typeof(float) => $"{ti:F6}",
            { } t when t == typeof(double) => $"{ti:F12}",
            _ => $"{ti:D}"
          };
        }

        return ts;
      }
    }

    private string[] GetAddress()
    {
      var ts = new string[Content.Length / 2];
      for (var i = 0; i < ts.Length; i++)
      {
        var ci = 2 * i;
        if (ci >= Content.Length) break;

        var address = StartAddress + i;

        ts[i] = $"{address:D5}";
      }

      return ts;
    }
  }

  #endregion
}
