using System.Globalization;
using System.Windows.Data;

namespace SbModbus.Tool.Models.ValueConverters;

public class EnumToBoolConverter : IValueConverter
{
  /// <summary>
  ///   分隔符，用于多个枚举值的情况
  /// </summary>
  public string Separator { get; set; } = ";";

  public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is null || parameter is null)
    {
      return false;
    }

    var valueString = value.ToString();
    var parameterString = parameter.ToString();

    if (string.IsNullOrEmpty(valueString) || string.IsNullOrEmpty(parameterString))
    {
      return false;
    }

    // 检查参数是否包含多个枚举值（用分隔符分隔）
    if (parameterString.Contains(Separator))
    {
      // 多个枚举值：只要匹配其中一个就返回true
      var parameters = parameterString.Split([Separator], StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Trim());

      return parameters.Any(p => valueString.Equals(p, StringComparison.OrdinalIgnoreCase));
    }

    // 单个枚举值
    return valueString.Equals(parameterString, StringComparison.OrdinalIgnoreCase);
  }

  public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is true && parameter != null)
    {
      var parameterString = parameter.ToString();
      if (string.IsNullOrEmpty(parameterString))
      {
        return null;
      }

      try
      {
        // 如果有多个参数，只取第一个
        if (parameterString.Contains(Separator))
        {
          parameterString = parameterString.Split([Separator], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim();

          if (string.IsNullOrEmpty(parameterString))
          {
            return null;
          }
        }

        return Enum.Parse(targetType, parameterString, true);
      }
      catch
      {
        return null;
      }
    }

    return null;
  }
}