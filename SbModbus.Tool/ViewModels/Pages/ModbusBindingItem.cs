using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sb.Extensions.System;
using SbModbus.Models;
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

public partial class ModbusBindingItem : ObservableObject
{
  private readonly Func<IModbusServer?> _getServer;

  public ModbusBindingItem(Func<IModbusServer?> getServer)
  {
    _getServer = getServer;
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

  public event Action<ModbusBindingItem>? DeleteRequested;

  [RelayCommand]
  private void DeleteSelf()
  {
    DeleteRequested?.Invoke(this);
  }

  [RelayCommand]
  private void Read()
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
