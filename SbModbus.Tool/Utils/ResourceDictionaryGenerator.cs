using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Controls;

namespace SbModbus.Tool.Utils;

public static class ResourceDictionaryGenerator
{
  /// <summary>
  ///   从字典生成 Avalonia 资源字典 XML 文件
  /// </summary>
  /// <param name="dictionary">键值对字典</param>
  /// <param name="outputPath">输出文件路径</param>
  /// <param name="resourceType">资源类型（String, Color, Brush 等）</param>
  public static void GenerateResourceDictionary(
    Dictionary<string, string> dictionary,
    string outputPath)
  {
    if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));

    if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));

    try
    {
      var sb = new StringBuilder();

      sb.AppendLine("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"");
      sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
      sb.AppendLine("                    xmlns:s=\"clr-namespace:System;assembly=mscorlib\">");
      sb.AppendLine("  <!-- ReSharper disable InconsistentNaming -->");

      foreach (var (key, value) in dictionary) sb.AppendLine(CreateResourceElement(key, value));

      sb.AppendLine("  <!-- ReSharper restore InconsistentNaming -->");
      sb.AppendLine("</ResourceDictionary>");


      // 确保输出目录存在
      var directory = Path.GetDirectoryName(outputPath);
      if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

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
  private static string CreateResourceElement(string key, string value)
  {
    return $"  <s:String x:Key=\"lang.{EscapeXml(key)}\">{EscapeXml(value)}</s:String>";
  }

  /// <summary>
  ///   XML 转义
  /// </summary>
  private static string EscapeXml(string text)
  {
    if (string.IsNullOrEmpty(text)) return string.Empty;

    return text
      .Replace("&", "&amp;")
      .Replace("<", "&lt;")
      .Replace(">", "&gt;")
      .Replace("\"", "&quot;")
      .Replace("'", "&apos;");
  }

  public static string? GetDictionarySource(IResourceDictionary dict)
  {
    // 尝试通过反射获取 Source（对于某些内部类型）
    try
    {
      var sourceProperty = dict.GetType().GetProperty("Source");
      if (sourceProperty != null) return sourceProperty.GetValue(dict) as string;
    }
    catch
    {
      // 忽略反射错误
    }

    return "Unknown";
  }
}