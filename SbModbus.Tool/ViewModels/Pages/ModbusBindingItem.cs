using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sb.Extensions.System;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Services.ModbusServer;
using SbModbus.Tool.Services.ModbusServices;

namespace SbModbus.Tool.ViewModels.Pages;

public enum ModbusBindingType
{
  Coil,
  DiscreteInput,
  HoldingRegister,
  InputRegister
}

public partial class ModbusBindingItem : ObservableObject, IDisposable
{
  private readonly Func<IModbusServer?> _getServer;
  private ModbusCoilsWriteHandler? _coilsWriteHandler;
  private ModbusRegistersWriteHandler? _registersWriteHandler;

  public ModbusBindingItem(Func<IModbusServer?> getServer)
  {
    _getServer = getServer;
  }

  public void Dispose()
  {
    DeactivateWriteHandlers();
  }

  [ObservableProperty]
  public partial ModbusBindingType BindingType { get; set; } = ModbusBindingType.HoldingRegister;

  [ObservableProperty]
  public partial ushort StartAddress { get; set; }

  [ObservableProperty]
  public partial string StartAddressString { get; set; } = "0";

  [ObservableProperty]
  public partial int StartAddressType { get; set; }

  partial void OnStartAddressStringChanged(string value)
  {
    try { StartAddress = Convert.ToUInt16(value, StartAddressType == 0 ? 10 : 16); } catch { }
  }

  partial void OnStartAddressTypeChanged(int value)
  {
    try { StartAddress = Convert.ToUInt16(StartAddressString, value == 0 ? 10 : 16); } catch { }
  }

  [ObservableProperty]
  public partial ModbusValueType ValueType { get; set; } = ModbusValueType.UShort;

  [ObservableProperty]
  public partial BigAndSmallEndianEncodingMode EndianMode { get; set; } = BigAndSmallEndianEncodingMode.ABCD;

  [ObservableProperty]
  public partial int DisplayMode { get; set; }

  [ObservableProperty]
  public partial string? DisplayValue { get; set; }

  [ObservableProperty]
  public partial bool CoilValue { get; set; }

  public bool IsRegisterType =>
    BindingType is ModbusBindingType.HoldingRegister or ModbusBindingType.InputRegister;

  partial void OnBindingTypeChanged(ModbusBindingType value)
  {
    OnPropertyChanged(nameof(IsRegisterType));
    if (value is ModbusBindingType.Coil or ModbusBindingType.DiscreteInput)
      ValueType = ModbusValueType.Bit;
    RegisterWriteHandler();
    ReadCore();
  }

  public static string[] ValueTypesForRegister { get; } =
    ["short", "ushort", "int", "uint", "long", "ulong", "float", "double"];

  public static string[] ValueTypesForCoil { get; } = ["bit"];

  public static string[] EndianModes { get; } = ["DCBA", "ABCD", "BADC", "CDAB"];

  private int RegisterCount => ValueType switch
  {
    ModbusValueType.Short or ModbusValueType.UShort => 1,
    ModbusValueType.Int or ModbusValueType.UInt or ModbusValueType.Float => 2,
    ModbusValueType.Long or ModbusValueType.ULong or ModbusValueType.Double => 4,
    _ => 1
  };

  partial void OnStartAddressChanged(ushort value)
  {
    RegisterWriteHandler();
    ReadCore();
  }

  partial void OnValueTypeChanged(ModbusValueType value)
  {
    RegisterWriteHandler();
    ReadCore();
  }

  /// <summary>激活写处理器：客户端写入时自动刷新显示值</summary>
  public void ActivateWriteHandlers()
  {
    RegisterWriteHandler();
  }

  /// <summary>移除写处理器</summary>
  public void DeactivateWriteHandlers()
  {
    UnregisterWriteHandler();
  }

  private void RegisterWriteHandler()
  {
    UnregisterWriteHandler();
    var server = _getServer();
    if (server is not { IsRunning: true }) return;

    try
    {
      switch (BindingType)
      {
        case ModbusBindingType.Coil:
          _coilsWriteHandler = new ModbusCoilsWriteHandler(StartAddress, 1, OnCoilWritten);
          server.AddHandler(_coilsWriteHandler);
          break;
        case ModbusBindingType.HoldingRegister:
          _registersWriteHandler = new ModbusRegistersWriteHandler(
            StartAddress, RegisterCount, OnRegisterWritten);
          server.AddHandler(_registersWriteHandler);
          break;
      }
    }
    catch { /* 忽略注册异常 */ }
  }

  private void UnregisterWriteHandler()
  {
    var server = _getServer();
    if (server == null) return;

    try
    {
      if (_coilsWriteHandler != null)
      {
        server.RemoveHandler(_coilsWriteHandler);
        _coilsWriteHandler = null;
      }
      if (_registersWriteHandler != null)
      {
        server.RemoveHandler(_registersWriteHandler);
        _registersWriteHandler = null;
      }
    }
    catch { }
  }

  private void OnCoilWritten(ModbusCoilsHandlerArgs args)
  {
    var value = args.Data[StartAddress];
    App.Current.Dispatcher.Invoke(() =>
    {
      CoilValue = value;
      DisplayValue = value ? "1" : "0";
    });
  }

  private void OnRegisterWritten(ModbusRegistersHandlerArgs args)
  {
    var span = args.Data.Data.Slice(StartAddress * 2, RegisterCount * 2);
    var displayValue = FormatRegisterValue(span);
    App.Current.Dispatcher.Invoke(() => DisplayValue = displayValue);
  }

  /// <summary>刷新显示值（从服务器读取当前数据）</summary>
  public void RefreshValue()
  {
    ReadCore();
  }

  public event Action<ModbusBindingItem>? DeleteRequested;

  [RelayCommand]
  private void DeleteSelf()
  {
    DeleteRequested?.Invoke(this);
  }

  [RelayCommand]
  private void Read()
  {
    ReadCore();
  }

  private void ReadCore()
  {
    var server = _getServer();
    if (server is not { IsRunning: true }) { DisplayValue = "N/A"; return; }

    try
    {
      switch (BindingType)
      {
        case ModbusBindingType.Coil or ModbusBindingType.DiscreteInput:
          using (var coils = (BindingType == ModbusBindingType.Coil ? server.Coils : server.DiscreteInputs).Lock())
          {
            CoilValue = coils[StartAddress];
            DisplayValue = CoilValue ? "1" : "0";
          }
          break;
        case ModbusBindingType.HoldingRegister or ModbusBindingType.InputRegister:
          using (var regs = (BindingType == ModbusBindingType.HoldingRegister
            ? server.HoldingRegisters : server.InputRegisters).Lock())
          {
            var span = regs.Data.Slice(StartAddress * 2, RegisterCount * 2);
            DisplayValue = FormatRegisterValue(span);
          }
          break;
      }
    }
    catch (Exception ex) { DisplayValue = $"Err: {ex.Message}"; }
  }

  [RelayCommand]
  private void Write()
  {
    var server = _getServer();
    if (server is not { IsRunning: true } || DisplayValue is null) return;

    try
    {
      switch (BindingType)
      {
        case ModbusBindingType.Coil:
          using (var coils = server.Coils.Lock()) { coils[StartAddress] = CoilValue; }
          break;
        case ModbusBindingType.HoldingRegister:
          using (var regs = server.HoldingRegisters.Lock())
            WriteRegisterValue(regs.Data.Slice(StartAddress * 2, RegisterCount * 2));
          break;
      }
    }
    catch (Exception ex) { DisplayValue = $"Err: {ex.Message}"; }
  }

  private string FormatRegisterValue(ReadOnlySpan<byte> span)
  {
    try
    {
      return ValueType switch
      {
        ModbusValueType.Short => Convert.ToInt16(span.ToT<short>(EndianMode))
          .ToString(DisplayMode == 0 ? "D" : "X"),
        ModbusValueType.UShort => Convert.ToUInt16(span.ToT<ushort>(EndianMode))
          .ToString(DisplayMode == 0 ? "D" : "X"),
        ModbusValueType.Int => span.ToT<int>(EndianMode)
          .ToString(DisplayMode == 0 ? "D" : "X"),
        ModbusValueType.UInt => span.ToT<uint>(EndianMode)
          .ToString(DisplayMode == 0 ? "D" : "X"),
        ModbusValueType.Float => span.ToT<float>(EndianMode).ToString("F6"),
        ModbusValueType.Long => span.ToT<long>(EndianMode)
          .ToString(DisplayMode == 0 ? "D" : "X"),
        ModbusValueType.ULong => span.ToT<ulong>(EndianMode)
          .ToString(DisplayMode == 0 ? "D" : "X"),
        ModbusValueType.Double => span.ToT<double>(EndianMode).ToString("F12"),
        _ => "?"
      };
    }
    catch { return "?"; }
  }

  private void WriteRegisterValue(Span<byte> span)
  {
    var fromBase = DisplayMode == 0 ? 10 : 16;
    try
    {
      switch (ValueType)
      {
        case ModbusValueType.Short:
          Convert.ToInt16(DisplayValue, fromBase).ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.UShort:
          Convert.ToUInt16(DisplayValue, fromBase).ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.Int:
          Convert.ToInt32(DisplayValue, fromBase).ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.UInt:
          Convert.ToUInt32(DisplayValue, fromBase).ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.Float when DisplayValue.TryParseToFloat(out var fv):
          fv.ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.Long:
          Convert.ToInt64(DisplayValue, fromBase).ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.ULong:
          Convert.ToUInt64(DisplayValue, fromBase).ToByteArray(EndianMode).CopyTo(span); break;
        case ModbusValueType.Double when DisplayValue.TryParseToDouble(out var dv):
          dv.ToByteArray(EndianMode).CopyTo(span); break;
      }
    }
    catch { }
  }
}
