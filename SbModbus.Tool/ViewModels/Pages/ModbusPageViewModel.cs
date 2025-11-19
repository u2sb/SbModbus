using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO.Ports;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SbModbus.Tool.Models.Settings;
using SbModbus.Tool.Services.DataTransferServices;
using SbModbus.Tool.Services.ModbusServices;
using SbModbus.Tool.Services.RecordServices;
using SbModbus.Tool.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using ObservableCollections;
using R3;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.SerialPortStream;
using SbModbus.Services.ModbusClient;
using SbModbus.TcpStream;

namespace SbModbus.Tool.ViewModels.Pages;

public partial class ModbusPageViewModel : ViewModelBase, IDisposable
{
  /// <summary>
  ///   日志记录器
  /// </summary>
  private readonly DataTransmissionRecord? _dtr;

  private AppSettings? _appSettings;

  private DisposableBag _disposableBag;

  /// <summary>
  ///   连接状态
  /// </summary>
  [ObservableProperty] private bool _isConnected;

  private IModbusClient? _modbusClient;

  /// <summary>
  ///   Modbus客户端模式
  /// </summary>
  [ObservableProperty] private ModbusClientType _modbusClientType = 0;

  private IModbusStream? _modbusStream;
  private ModbusPageSettings? _settings;


  public ModbusPageViewModel()
  {
    if (!Design.IsDesignMode)
    {
      _dtr = Ioc.Default.GetRequiredService<DataTransmissionRecord>();
      OnLoaded();
      LoadSettings();
    }

    SerialPortNames =
      _serialPortNames.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    SerialPortNames.AddTo(ref _disposableBag);

    DsLogs = _dsLogs.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    DsLogs.AddTo(ref _disposableBag);
    _dsLogs.ObserveAdd().Subscribe(OnDsLogsAddItem).AddTo(ref _disposableBag);

    MsLogs = _msLogs.ToNotifyCollectionChanged(SynchronizationContextCollectionEventDispatcher.Current);
    MsLogs.AddTo(ref _disposableBag);
    _msLogs.ObserveAdd().Subscribe(OnMsLogsAddItem).AddTo(ref _disposableBag);
  }

  public void Dispose()
  {
    _modbusStream?.Dispose();
    _disposableBag.Dispose();
  }

  partial void OnIsConnectedChanged(bool value)
  {
    OnConnectStateChanged(value);
  }

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
  ///   数据位列表
  /// </summary>
  public int[] DataBitsValues { get; } = [8, 7, 9, 10];

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
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsIp))]
  private string _targetIp = "127.0.0.1";

  /// <summary>
  ///   目标端口
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsUInt16))]
  private string _targetPort = "502";

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

  #endregion

  #region Modbus读写

  /// <summary>
  ///   站号
  /// </summary>
  [ObservableProperty] private byte _stationId = 1;

  /// <summary>
  ///   站号字符串
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsByte))]
  private string? _stationIdString = "1";

  partial void OnStationIdStringChanged(string? value)
  {
    if (value is not null && value.TryParseToByte(out var sid))
    {
      StationId = sid;
    }
  }
  
  /// <summary>
  /// 编码方式
  /// </summary>
  [ObservableProperty] private int _encodingModeIndex = (int)BigAndSmallEndianEncodingMode.ABCD;
  
  partial void OnEncodingModeIndexChanged(int value)
  {
    EncodingMode = (BigAndSmallEndianEncodingMode)value;
  }

  /// <summary>
  ///   编码方式
  /// </summary>
  [ObservableProperty] private BigAndSmallEndianEncodingMode _encodingMode = BigAndSmallEndianEncodingMode.ABCD;

  /// <summary>
  ///   编码方式
  /// </summary>
  public string[] EncodingModes { get; } = ["DCBA", "ABCD", "BADC", "CDAB"];

  /// <summary>
  ///   被选中的写方法
  /// </summary>
  [ObservableProperty] private int _selectedModbusWriteFunCodeIndex = 2;

  /// <summary>
  ///   选择的方法
  /// </summary>
  public ModbusFunctionCode SelectedModbusWriteFunctionCode => SelectedModbusWriteFunCodeIndex switch
  {
    0 => ModbusFunctionCode.WriteSingleCoil,
    1 => ModbusFunctionCode.WriteSingleRegister,
    2 => ModbusFunctionCode.WriteMultipleRegisters,
    _ => ModbusFunctionCode.Error
  };

  /// <summary>
  ///   被选中的写FunCode
  /// </summary>
  [ObservableProperty] private int _selectedModbusReadFunCodeIndex = 2;

  /// <summary>
  ///   选择的方法
  /// </summary>
  public ModbusFunctionCode SelectedModbusReadFunctionCode => SelectedModbusReadFunCodeIndex switch
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
  [ObservableProperty] private ushort _modbusAddress = 1;

  /// <summary>
  ///   地址
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsUInt16))]
  private string? _modbusAddressString = "1";

  partial void OnModbusAddressStringChanged(string? value)
  {
    if (value is not null && value.TryParseToUInt16(out var address))
    {
      ModbusAddress = address;
    }
  }

  /// <summary>
  ///   寄存器数量
  /// </summary>
  [ObservableProperty] private byte _readNumber = 1;

  /// <summary>
  ///   寄存器数量
  /// </summary>
  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsByte))]
  private string? _readNumberString = "1";

  partial void OnReadNumberStringChanged(string? value)
  {
    if (value is not null && value.TryParseToByte(out var number))
    {
      ReadNumber = number;
    }
  }

  /// <summary>
  ///   Modbus值类型
  /// </summary>
  [ObservableProperty] private ModbusValueType _selectedModbusValueType = ModbusValueType.UShort;

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


  [ObservableProperty] private double _modbusInputValue;

  [ObservableProperty] [CustomValidation(typeof(SbValidation), nameof(SbValidation.StringIsDouble))]
  private string? _modbusInputValueString = "0";

  partial void OnModbusInputValueStringChanged(string? value)
  {
    if (value is not null && value.TryParseToDouble(out var inputValue))
    {
      ModbusInputValue = inputValue;
    }
  }

  /// <summary>
  ///   读
  /// </summary>
  [RelayCommand]
  private async Task ReadAsync(CancellationToken ct)
  {
    if (_modbusClient is not { IsConnected: true })
    {
      return;
    }

    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(3));
    var rc = ModbusReadCount;

    try
    {
      var result = SelectedModbusReadFunctionCode switch
      {
        ModbusFunctionCode.ReadCoils =>
          await _modbusClient.ReadCoilsAsync(StationId, ModbusAddress, rc, cts.Token),
        ModbusFunctionCode.ReadDiscreteInputs =>
          await _modbusClient.ReadDiscreteInputsAsync(StationId, ModbusAddress, rc, cts.Token),
        ModbusFunctionCode.ReadHoldingRegisters =>
          await _modbusClient.ReadHoldingRegistersAsync(StationId, ModbusAddress, rc, cts.Token),
        ModbusFunctionCode.ReadInputRegisters =>
          await _modbusClient.ReadInputRegistersAsync(StationId, ModbusAddress, rc, cts.Token),
        _ => Memory<byte>.Empty
      };

      if (!result.IsEmpty)
      {
        _msLogs.Enqueue(new ModbusReadLog(DateTime.Now, result.ToArray(), EncodingMode, ModbusAddress));
      }
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
    if (_modbusClient is not { IsConnected: true })
    {
      return;
    }

    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(3));

    try
    {
      switch (SelectedModbusWriteFunctionCode)
      {
        case ModbusFunctionCode.WriteSingleCoil:
          var bv = ModbusInputValue > 0;
          await _modbusClient.WriteSingleCoilAsync(StationId, ModbusAddress, bv, cts.Token);
          break;
        case ModbusFunctionCode.WriteSingleRegister:
          byte[] srv = [];

          if (ModbusInputValueString is not null)
          {
            srv = SelectedModbusValueType switch
            {
              ModbusValueType.Short when ModbusInputValueString.TryParseToInt16(out var sv) =>
                sv.ToByteArray(EncodingMode),
              ModbusValueType.UShort when ModbusInputValueString.TryParseToUInt16(out var usv) =>
                usv.ToByteArray(EncodingMode),
              _ => srv
            };
          }

          if (srv.Length == 2)
          {
            await _modbusClient.WriteSingleRegisterAsync(StationId, ModbusAddress, srv, cts.Token);
          }

          break;
        case ModbusFunctionCode.WriteMultipleRegisters:
          byte[] mrv = [];

          if (ModbusInputValueString is not null)
          {
            mrv = SelectedModbusValueType switch
            {
              ModbusValueType.Short when ModbusInputValueString.TryParseToInt16(out var sv) =>
                sv.ToByteArray(EncodingMode),

              ModbusValueType.UShort when ModbusInputValueString.TryParseToUInt16(out var usv) =>
                usv.ToByteArray(EncodingMode),

              ModbusValueType.Int when ModbusInputValueString.TryParseToInt32(out var iv) =>
                iv.ToByteArray(EncodingMode),

              ModbusValueType.UInt when ModbusInputValueString.TryParseToUInt32(out var uiv) =>
                uiv.ToByteArray(EncodingMode),

              ModbusValueType.Long when ModbusInputValueString.TryParseToInt64(out var lv) =>
                lv.ToByteArray(EncodingMode),

              ModbusValueType.ULong when ModbusInputValueString.TryParseToUInt64(out var ulv) =>
                ulv.ToByteArray(EncodingMode),

              ModbusValueType.Float when ModbusInputValueString.TryParseToFloat(out var fv) =>
                fv.ToByteArray(EncodingMode),

              ModbusValueType.Double when ModbusInputValueString.TryParseToDouble(out var dv) =>
                dv.ToByteArray(EncodingMode),

              _ => mrv
            };
          }

          if (mrv.Length > 0)
          {
            await _modbusClient.WriteMultipleRegistersAsync(StationId, ModbusAddress, mrv, cts.Token);
          }

          break;
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
    }

    if (_modbusStream is not null)
    {
      try
      {
        _modbusStream.ReadTimeout = 2000;
        _modbusStream.WriteTimeout = 2000;
        IsConnected = _modbusStream.Connect();
      }
      catch (Exception)
      {
        // ignore
      }

      if (IsConnected)
      {
        _modbusStream.OnRead += OnDataReceived;
        _modbusStream.OnWrite += OnDataWrite;
      }
    }

    await SaveSettingsAsync();
  }

  [RelayCommand]
  private void Disconnect()
  {
    if (_modbusStream is null)
    {
      return;
    }

    if (_modbusStream.IsConnected)
    {
      _modbusStream.Disconnect();
    }

    _modbusStream.OnRead -= OnDataReceived;
    _modbusStream.OnWrite -= OnDataWrite;

    IsConnected = false;

    _modbusStream.Dispose();
    _modbusStream = null;
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

  private void OnDataWrite(ReadOnlySpan<byte> data, IModbusStream _)
  {
    _dtr?.WriteOutLog(data);
    _dsLogs.Enqueue(new DsLog(DateTime.Now, data.ToArray(), true));
  }

  private void OnDataReceived(ReadOnlySpan<byte> data, IModbusStream _)
  {
    _dtr?.WriteInLog(data);
    _dsLogs.Enqueue(new DsLog(DateTime.Now, data.ToArray(), false));
  }

  #endregion

  #region 加载和保存配置文件

  [RelayCommand]
  private void OnLoaded()
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
    _settings = _appSettings?.Modbus;


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
      if (TargetPort.TryParseToUInt16(out var targetPort))
      {
        _settings.TargetPort = targetPort;
      }

      _settings.StationId = StationId;

      await _appSettings.SaveAsync();
    }
  }

  #endregion

  #region 日志显示框

  private readonly ObservableQueue<DsLog> _dsLogs = new();

  /// <summary>
  ///   显示的日志
  /// </summary>
  public INotifyCollectionChangedSynchronizedViewList<DsLog> DsLogs { get; }

  private void OnDsLogsAddItem(CollectionAddEvent<DsLog> _)
  {
    if (_dsLogs.Count > DsLogMaxShow)
    {
      _dsLogs.Dequeue();
    }
  }

  [ObservableProperty] private int _dsLogMaxShow = 50;

  [ObservableProperty] private Vector _dsLogShowOffset;
  [ObservableProperty] private Size _dsLogShowExtentSize;
  [ObservableProperty] private Size _dsLogShowViewPortSize;

  partial void OnDsLogShowExtentSizeChanged(Size oldValue, Size newValue)
  {
    if (DsLogShowOffset.Y + DsLogShowViewPortSize.Height >= oldValue.Height - 10.0)
    {
      DsLogShowOffset = DsLogShowOffset.WithY(newValue.Height - DsLogShowViewPortSize.Height);
    }
  }

  private readonly ObservableQueue<ModbusReadLog> _msLogs = new();

  public INotifyCollectionChangedSynchronizedViewList<ModbusReadLog> MsLogs { get; }

  private void OnMsLogsAddItem(CollectionAddEvent<ModbusReadLog> _)
  {
    if (_msLogs.Count > DsLogMaxShow)
    {
      _msLogs.Dequeue();
    }
  }

  [ObservableProperty] private Vector _msLogShowOffset;
  [ObservableProperty] private Size _msLogShowExtentSize;
  [ObservableProperty] private Size _msLogShowViewPortSize;

  partial void OnMsLogShowExtentSizeChanged(Size oldValue, Size newValue)
  {
    if (MsLogShowOffset.Y + MsLogShowViewPortSize.Height >= oldValue.Height - 10.0)
    {
      MsLogShowOffset = MsLogShowOffset.WithY(newValue.Height - MsLogShowViewPortSize.Height);
    }
  }

  [RelayCommand]
  private void CleanOutput()
  {
    _dsLogs.Clear();
    _msLogs.Clear();
  }

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
          if (ci >= Content.Length)
          {
            break;
          }

          var ti = csp.Slice(ci, size);

          var bit = new BitSpan(ti);
          var sb = new StringBuilder();

          for (var j = 0; j < bit.Length; j++)
          {
            var b = bit[j];
            sb.Append(b ? '1' : '0');
            if (j == 7)
            {
              sb.Append(' ');
            }
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
          if (ci >= Content.Length)
          {
            break;
          }

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
        if (ci >= Content.Length)
        {
          break;
        }

        var address = StartAddress + i;

        ts[i] = $"{address:D5}";
      }

      return ts;
    }
  }

  #endregion
}