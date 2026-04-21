using System.Text.Json;

namespace SbModbus.Tool.Utils.Serializer;

internal static class SbModbusJsonSerializer
{
  public static JsonSerializerOptions DefaultOptions => SbModbusJsonSerializerOptions.Default;

  /// <summary>
  ///   序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="obj"></param>
  /// <param name="type"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static byte[] Serialize<T>(T obj, Type? type = null,
    JsonSerializerOptions? options = null)
  {
    type ??= typeof(T);
    options ??= DefaultOptions;

    var bs = JsonSerializer.SerializeToUtf8Bytes(obj, type, options);

    return bs;
  }

  /// <summary>
  ///   序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="obj"></param>
  /// <param name="type"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static string SerializeToString<T>(T obj, Type? type = null, JsonSerializerOptions? options = null)
  {
    type ??= typeof(T);
    options ??= DefaultOptions;

    return JsonSerializer.Serialize(obj, type, options);
  }

  /// <summary>
  ///   异步序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="stream"></param>
  /// <param name="obj"></param>
  /// <param name="type"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static async Task SerializeAsync<T>(Stream stream, T obj, Type? type = null,
    JsonSerializerOptions? options = null)
  {
    type ??= typeof(T);
    options ??= DefaultOptions;

    await JsonSerializer.SerializeAsync(stream, obj, type, options);
  }

  /// <summary>
  ///   反序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="data"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static T? Deserialize<T>(ReadOnlySpan<byte> data, JsonSerializerOptions? options = null)
  {
    options ??= DefaultOptions;
    return JsonSerializer.Deserialize<T>(data, options);
  }

  /// <summary>
  ///   反序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="stream"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static T? Deserialize<T>(Stream stream, JsonSerializerOptions? options = null)
  {
    options ??= DefaultOptions;

    return JsonSerializer.Deserialize<T>(stream, options);
  }

  /// <summary>
  ///   反序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="data"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static T? Deserialize<T>(string data, JsonSerializerOptions? options = null)
  {
    options ??= DefaultOptions;

    return JsonSerializer.Deserialize<T>(data, options);
  }

  /// <summary>
  ///   异步反序列化
  /// </summary>
  /// <typeparam name="T"></typeparam>
  /// <param name="stream"></param>
  /// <param name="options"></param>
  /// <returns></returns>
  public static async Task<T?> DeserializeAsync<T>(Stream stream, JsonSerializerOptions? options = null)
  {
    options ??= DefaultOptions;

    return await JsonSerializer.DeserializeAsync<T>(stream, options);
  }
}
