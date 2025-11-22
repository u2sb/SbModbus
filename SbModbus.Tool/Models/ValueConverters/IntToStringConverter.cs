using System.Globalization;
using System.Windows.Data;

namespace SbModbus.Tool.Models.ValueConverters;

public class IntToStringConverter : IValueConverter
{
  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    // 处理null值
    if (value == null)
    {
      return parameter as string ?? string.Empty;
    }

    // int转string
    if (value is int intValue)
    {
      return intValue.ToString(culture ?? CultureInfo.CurrentCulture);
    }

    // 尝试转换其他类型
    try
    {
      return System.Convert.ToInt32(value).ToString(culture ?? CultureInfo.CurrentCulture);
    }
    catch
    {
      return "0";
    }
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    // 处理null值
    if (value == null)
    {
      if (targetType == typeof(int?))
      {
        return null;
      }

      return 0;
    }

    // 处理空字符串
    if (value is string str && string.IsNullOrWhiteSpace(str))
    {
      if (targetType == typeof(int?))
      {
        return null;
      }

      return 0;
    }

    // string转int
    if (value is string stringValue)
    {
      if (int.TryParse(stringValue, NumberStyles.Integer, culture ?? CultureInfo.CurrentCulture, out var result))
      {
        if (targetType == typeof(int?))
        {
          return result;
        }

        return result;
      }
    }

    // 其他类型尝试转换
    try
    {
      var converted = System.Convert.ToInt32(value);
      if (targetType == typeof(int?))
      {
        return converted;
      }

      return converted;
    }
    catch
    {
      if (targetType == typeof(int?))
      {
        return null;
      }

      return 0;
    }
  }
}