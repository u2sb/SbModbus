using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SbModbus.Tool.Models.ValueConverters;

public class EnumToVisibilityConverter : EnumToBoolConverter
{
  public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var result = (bool)(base.Convert(value, typeof(bool), parameter, culture) ?? false);
    return result ? Visibility.Visible : Visibility.Collapsed;
  }

  public override object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    var result = value is Visibility.Visible;
    return base.ConvertBack(result, targetType, parameter, culture);
  }
}