using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using SbModbus.Tool.Services.RecordServices;

namespace SbModbus.Tool.Models;

/// <summary>
///   传输数据日志记录
/// </summary>
public record DsLog(DateTime DateTime, byte[] Content, bool IsOut)
{
  public string DateTimeString => $"[{DateTime:yyyy-MM-dd HH:mm:ss.fff}]";

  public SolidColorBrush TextColor => new(IsOut ? Colors.Blue : Colors.Green);

  public string HexString
  {
    get
    {
      var len = Content.Length * 3 - 1;
      if (len < 0) return string.Empty;

      var temp = len < 512 ? stackalloc char[len] : new char[len];
      DataTransmissionRecordLogExtensions.WriteHexChar(Content, temp);
      return temp.ToString();
    }
  }
}
