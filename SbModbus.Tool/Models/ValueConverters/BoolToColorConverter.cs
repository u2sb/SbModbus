using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SbModbus.Tool.Models.ValueConverters;

public class BoolToColorBrushConverter : IValueConverter
{
  public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is bool boolValue)
    {
      if (parameter is string and ("!" or "not")) boolValue = !boolValue;

      return boolValue ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
    }

    // 默认颜色（比如当值为null时）
    return new SolidColorBrush(Colors.Red);
  }

  public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is not SolidColorBrush sb) return false;
    var result = sb.Color == Colors.Green;

    if (parameter is string and ("!" or "not")) result = !result;
    return result;
  }
}