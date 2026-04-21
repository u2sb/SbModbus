#:sdk Microsoft.NET.Sdk
#:property TargetFramework=net10.0
#:property LangVersion=preview

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


const string LF = "\r\n";

var projectDir = args[0];

if (Directory.Exists(projectDir))
{
  var source = Path.Combine(projectDir, "i18n", "zh-CN.json");
  var target = Path.Combine(projectDir, "Resources", "Language.xaml");

  if (File.Exists(source))
  {
    try
    {
      var bs = File.ReadAllBytes(source);
      var dic = JsonSerializer.Deserialize(bs, DictionaryStringStringContext.Default.DictionaryStringString) ?? [];
      GenerateResourceDictionary(dic, target);
      Console.WriteLine($"成功生成资源字典: {target}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"生成资源字典失败: {ex.Message}");
    }
  }
  else
  {
    Console.WriteLine($"源文件不存在: {source}");
  }

}
else
{
  Console.WriteLine($"无效的项目目录: {projectDir}");
}


return;

/// <summary>
///   从字典生成 Avalonia 资源字典 XML 文件
/// </summary>
/// <param name="dictionary">键值对字典</param>
/// <param name="outputPath">输出文件路径</param>
static void GenerateResourceDictionary(Dictionary<string, string> dictionary, string outputPath)
{
  if (dictionary == null)
  {
    throw new ArgumentNullException(nameof(dictionary));
  }

  if (string.IsNullOrEmpty(outputPath))
  {
    throw new ArgumentNullException(nameof(outputPath));
  }

  try
  {
    var sb = new StringBuilder();

    sb.Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"").Append(LF);
    sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"").Append(LF);
    sb.Append("                    xmlns:s=\"clr-namespace:System;assembly=mscorlib\">").Append(LF);
    sb.Append("  <!-- ReSharper disable InconsistentNaming -->").Append(LF);

    foreach (var item in dictionary)
    {
      sb.Append(CreateResourceElement(item.Key, item.Value)).Append(LF);
    }

    sb.Append("  <!-- ReSharper restore InconsistentNaming -->").Append(LF);
    sb.Append("</ResourceDictionary>");

    // 确保输出目录存在
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    using var fs = File.CreateText(outputPath);
    fs.Write(sb.ToString());
  }
  catch (Exception ex)
  {
    throw new InvalidOperationException($"生成资源字典失败: {ex.Message}", ex);
  }
}

/// <summary>
///   创建单个资源元素
/// </summary>
static string CreateResourceElement(string key, string value)
{
  return $"  <s:String x:Key=\"lang.{EscapeXml(key)}\">{EscapeXml(value)}</s:String>";
}

/// <summary>
///   XML 转义
/// </summary>
static string EscapeXml(string text)
{
  if (string.IsNullOrEmpty(text))
  {
    return string.Empty;
  }

  return text
    .Replace("&", "&amp;")
    .Replace("<", "&lt;")
    .Replace(">", "&gt;")
    .Replace("\"", "&quot;")
    .Replace("'", "&apos;");
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class DictionaryStringStringContext : JsonSerializerContext
{
}