using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SbModbus.Tool.Models.ValueConverters;

public class BoolInverseConverter : IValueConverter
{
  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is bool boolValue)
      return !boolValue;
    return value;
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is bool boolValue)
      return !boolValue;
    return value;
  }
}