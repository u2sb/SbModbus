using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SbModbus.Tool.Utils;
using CommunityToolkit.HighPerformance.Buffers;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace SbModbus.Tool.Services.RecordServices;

/// <summary>
///   数据传输记录器
/// </summary>
public class DataTransmissionRecord : IDisposable
{
  private ILogger<DataTransmissionRecord>? _logger;

  private ILoggerFactory? _loggerFactory;

  public static string LogDirPath =>
    Path.Combine(ZioFile.Instance.RootPath.FullName, "Logs", "TransmissionRecord", $"{DateTime.Now:yyyy/MM/dd/}");

  private static string LogFilePath => Path.Combine(LogDirPath, $"{DateTime.Now:HH-mm-ss-fff}.ansi");

  public void Dispose()
  {
    _loggerFactory?.Dispose();
    GC.SuppressFinalize(this);
  }

  /// <summary>
  ///   创建新的记录器
  /// </summary>
  public void Open()
  {
    if (!Directory.Exists(LogDirPath)) Directory.CreateDirectory(LogDirPath);

    Close();

    _loggerFactory = LoggerFactory.Create(logging =>
    {
      logging.ClearProviders();
      logging.SetMinimumLevel(LogLevel.Trace);
      logging.AddZLoggerFile(LogFilePath);
#if DEBUG
      logging.AddZLoggerConsole(options => { options.ConfigureEnableAnsiEscapeCode = true; });
#endif
    });

    _logger = _loggerFactory.CreateLogger<DataTransmissionRecord>();
  }

  public void Close()
  {
    if (_loggerFactory is not null)
    {
      _loggerFactory.Dispose();
      _loggerFactory = null;
    }
  }

  /// <summary>
  ///   写 输出日志
  /// </summary>
  /// <param name="data"></param>
  /// <param name="isAscii"></param>
  /// <returns>字符串格式的data</returns>
  public void WriteOutLog(ReadOnlySpan<byte> data, bool isAscii = false)
  {
    if (isAscii)
      _logger?.WriteAsciiLog(data);
    else
      _logger?.WriteHexLog(data);
  }

  /// <summary>
  ///   写输入日志
  /// </summary>
  /// <param name="data"></param>
  /// <param name="isAscii"></param>
  /// <returns>字符串格式的data</returns>
  public void WriteInLog(ReadOnlySpan<byte> data, bool isAscii = false)
  {
    if (isAscii)
      _logger?.ReadAsciiLog(data);
    else
      _logger?.ReadHexLog(data);
  }
}

public static class DataTransmissionRecordLogExtensions
{
  private const string HexDigits = "0123456789ABCDEF";

  private const string OutPutColor = "\e[34m";
  private const string InPutColor = "\e[32m";
  private const string NoneColor = "\e[0m";
  private static readonly StringPool LogAsciiPool = new();
  private static readonly StringPool LogHexPool = new();

  public static void WriteHexChar(ReadOnlySpan<byte> data, Span<char> hexChars)
  {
    if (data.IsEmpty) return;
    var requiredLength = data.Length * 3 - 1;
    if (requiredLength < 0)
    {
      return;
    }
    if (hexChars.Length < requiredLength)
      throw new ArgumentException($"输出缓冲区需要至少 {requiredLength} 字符，但只有 {hexChars.Length}");

    ReadOnlySpan<char> hexDigits = HexDigits;

    ref var dataRef = ref MemoryMarshal.GetReference(data);
    ref var charsRef = ref MemoryMarshal.GetReference(hexChars);

    var dataLength = data.Length;
    var lastIndex = dataLength - 1;

    for (var i = 0; i < dataLength; i++)
    {
      var b = Unsafe.Add(ref dataRef, i);
      var baseIndex = i * 3;

      Unsafe.Add(ref charsRef, baseIndex) = hexDigits[b >> 4];
      Unsafe.Add(ref charsRef, baseIndex + 1) = hexDigits[b & 0x0F];

      if (i < lastIndex)
        Unsafe.Add(ref charsRef, baseIndex + 2) = ' ';
    }
  }


  extension(ILogger<DataTransmissionRecord> logger)
  {
    /// <summary>
    ///   写入写日志
    /// </summary>
    /// <param name="data"></param>
    /// <param name="encoding"></param>
    public void WriteAsciiLog(ReadOnlySpan<byte> data, Encoding? encoding = null)
    {
      if (data.IsEmpty) return;

      encoding ??= Encoding.UTF8;

      var ds = LogAsciiPool.GetOrAdd(data, encoding);

      logger.ZLogInformation($"{NoneColor}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [O] {OutPutColor}{ds}{NoneColor}");
    }


    /// <summary>
    ///   写入写日志
    /// </summary>
    /// <param name="data"></param>
    public void WriteHexLog(ReadOnlySpan<byte> data)
    {
      if (data.IsEmpty) return;

      Span<char> hexChars = stackalloc char[data.Length * 3 - 1];

      WriteHexChar(data, hexChars);

      var ds = LogHexPool.GetOrAdd(hexChars);

      logger.ZLogInformation($"{NoneColor}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [O] {OutPutColor}{ds}{NoneColor}");
    }

    /// <summary>
    ///   写入读日志
    /// </summary>
    /// <param name="data"></param>
    /// <param name="encoding"></param>
    public void ReadAsciiLog(ReadOnlySpan<byte> data, Encoding? encoding = null)
    {
      if (data.IsEmpty) return;

      encoding ??= Encoding.UTF8;

      var ds = LogAsciiPool.GetOrAdd(data, encoding);
      logger.ZLogInformation($"{NoneColor}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [I] {InPutColor}{ds}{NoneColor}");
    }

    /// <summary>
    ///   写入读日志
    /// </summary>
    /// <param name="data"></param>
    public void ReadHexLog(ReadOnlySpan<byte> data)
    {
      if (data.IsEmpty) return;

      Span<char> hexChars = stackalloc char[data.Length * 3 - 1];

      WriteHexChar(data, hexChars);

      var ds = LogHexPool.GetOrAdd(hexChars);

      logger.ZLogInformation($"{NoneColor}[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [I] {InPutColor}{ds}{NoneColor}");
    }
  }
}