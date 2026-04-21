using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbModbus.Tool.Utils.Serializer;

internal static class SbModbusJsonSerializerOptions
{
  static SbModbusJsonSerializerOptions()
  {
    JsonConverter[] fms = [new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)];

    foreach (var converter in fms)
    {
      Default.Converters.Add(converter);
      DefaultWithIndented.Converters.Add(converter);
    }
  }

  public static JsonSerializerOptions Default { get; } = new(JsonSerializerDefaults.Web)
  {
    PropertyNameCaseInsensitive = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
  };

  /// <summary>
  ///   带有缩进的默认选项
  /// </summary>
  public static JsonSerializerOptions DefaultWithIndented { get; } = new(Default)
  {
    WriteIndented = true,
    IndentSize = 2
  };

  public static void SetJsonSerializerOptions(JsonSerializerOptions options)
  {
    options.PropertyNameCaseInsensitive = Default.PropertyNameCaseInsensitive;
    options.Encoder = Default.Encoder;

    foreach (var converter in Default.Converters) options.Converters.Add(converter);
  }
}
