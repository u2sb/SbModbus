using System.Globalization;
using System.Windows.Data;

namespace SbModbus.Tool.Models.ValueConverters;

public class EnumToIntConverter : IValueConverter
{
  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value == null)
    {
      return 0;
    }

    // 如果已经是int类型，直接返回
    if (value is int intValue)
    {
      return intValue;
    }

    // 如果是枚举类型，转换为int
    if (value is Enum enumValue)
    {
      return System.Convert.ToInt32(enumValue);
    }

    // 尝试将其他类型转换为int
    try
    {
      return System.Convert.ToInt32(value);
    }
    catch
    {
      return 0; // 转换失败返回默认值
    }
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value == null)
    {
      return GetDefaultEnumValue(targetType);
    }

    try
    {
      // 如果目标类型是枚举
      if (targetType.IsEnum)
      {
        var intValue = System.Convert.ToInt32(value);
        return Enum.ToObject(targetType, intValue);
      }

      // 如果目标类型是int
      if (targetType == typeof(int))
      {
        return System.Convert.ToInt32(value);
      }

      // 如果目标类型是可为空的int
      if (targetType == typeof(int?))
      {
        return System.Convert.ToInt32(value);
      }

      // 如果目标类型是可为空的枚举
      if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
      {
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType?.IsEnum == true)
        {
          var intValue = System.Convert.ToInt32(value);
          return Enum.ToObject(underlyingType, intValue);
        }
      }

      return GetDefaultEnumValue(targetType);
    }
    catch
    {
      return GetDefaultEnumValue(targetType);
    }
  }

  private object? GetDefaultEnumValue(Type targetType)
  {
    // 返回枚举的第一个值作为默认值，或者0
    if (targetType.IsEnum)
    {
      var values = Enum.GetValues(targetType);
      return values.Length > 0 ? values.GetValue(0) : 0;
    }

    // 如果是可为空的枚举，返回null
    if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
    {
      var underlyingType = Nullable.GetUnderlyingType(targetType);
      if (underlyingType?.IsEnum == true)
      {
        return null;
      }
    }

    return 0;
  }
}