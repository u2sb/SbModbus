using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace SbModbus.Utils;

/// <summary>
///   日志等级（与 Microsoft.Extensions.Logging.LogLevel 值一致）
/// </summary>
public enum LogLevel
{
  /// <summary>Trace = 0</summary>
  Trace = 0,

  /// <summary>Debug = 1</summary>
  Debug = 1,

  /// <summary>Information = 2</summary>
  Information = 2,

  /// <summary>Warning = 3</summary>
  Warning = 3,

  /// <summary>Error = 4</summary>
  Error = 4,

  /// <summary>Critical = 5</summary>
  Critical = 5,

  /// <summary>None = 6</summary>
  None = 6
}

/// <summary>
///   插值字符串处理器 — 日志等级不足时零分配
/// </summary>
[InterpolatedStringHandler]
public readonly ref struct LoggerInterpolatedStringHandler
{
  private readonly StringBuilder? _builder;

  /// <summary>当前处理器是否启用（即 StringBuilder 已分配）</summary>
  public bool IsEnabled => _builder != null;

  /// <summary>
  ///   由编译器调用的构造函数
  /// </summary>
  /// <param name="literalLength">字面量总长度</param>
  /// <param name="formattedCount">插值表达式数量</param>
  /// <param name="level">目标日志等级</param>
  /// <param name="isEnabled">输出：该日志等级是否启用</param>
  public LoggerInterpolatedStringHandler(int literalLength, int formattedCount, LogLevel level, out bool isEnabled)
  {
    isEnabled = Logger.IsEnabled(level);
    _builder = isEnabled ? new StringBuilder(literalLength) : null;
  }

  /// <summary>
  /// </summary>
  /// <param name="s"></param>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendLiteral(string s)
  {
    _builder?.Append(s);
  }


  /// <summary>
  /// </summary>
  /// <param name="value"></param>
  /// <typeparam name="T"></typeparam>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted<T>(T value)
  {
    _builder?.Append(value);
  }


  /// <summary>
  /// </summary>
  /// <param name="value"></param>
  /// <param name="format"></param>
  /// <typeparam name="T"></typeparam>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted<T>(T value, string? format)
  {
    if (_builder != null)
    {
      if (format != null)
        _builder.AppendFormat(null, $"{{0:{format}}}", value);
      else
        _builder.Append(value);
    }
  }

  /// <summary>获取最终拼接的日志字符串</summary>
  public readonly string GetResult()
  {
    return _builder?.ToString() ?? string.Empty;
  }
}

/// <summary>
///   静态日志类 — 替代 Microsoft.Extensions.Logging.ILogger
/// </summary>
/// <remarks>
///   使用方式：
///   <list type="bullet">
///     <item><c>Logger.Output = (level, msg) => Console.WriteLine(msg);</c> — 注入写操作委托</item>
///     <item><c>Logger.Level = LogLevel.Debug;</c> — 配置全局日志等级</item>
///     <item><c>Logger.Log(LogLevel.Debug, $"count={count}");</c> — 插值字符串（零分配，推荐）</item>
///     <item><c>Logger.Information("connected");</c> — 纯字符串</item>
///     <item><c>Logger.Error(ex, "failed");</c> — 异常日志</item>
///   </list>
/// </remarks>
public static class Logger
{
  /// <summary>全局日志等级，默认 <see cref="LogLevel.Information" /></summary>
  public static LogLevel Level { get; set; } = LogLevel.Information;

  /// <summary>外部注入的日志输出委托</summary>
  public static Action<LogLevel, string>? Output { get; set; }

  /// <summary>检查指定等级的日志是否启用</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static bool IsEnabled(LogLevel level)
  {
    return level >= Level && Output != null;
  }

  #region 主方法（插值字符串 — 零分配）

  /// <summary>
  ///   写入日志（插值字符串，零分配）
  /// </summary>
  public static void Log(LogLevel level,
    [InterpolatedStringHandlerArgument("level")]
    ref LoggerInterpolatedStringHandler handler)
  {
    if (handler.IsEnabled)
      Output!(level, handler.GetResult());
  }

  #endregion

  #region FormattableString 重载（格式化延迟求值）

  /// <inheritdoc cref="Log" />
  public static void Trace(FormattableString message)
  {
    if (IsEnabled(LogLevel.Trace)) Output!(LogLevel.Trace, message.ToString(null));
  }

  /// <inheritdoc cref="Log" />
  public static void Debug(FormattableString message)
  {
    if (IsEnabled(LogLevel.Debug)) Output!(LogLevel.Debug, message.ToString(null));
  }

  /// <inheritdoc cref="Log" />
  public static void Information(FormattableString message)
  {
    if (IsEnabled(LogLevel.Information)) Output!(LogLevel.Information, message.ToString(null));
  }

  /// <inheritdoc cref="Log" />
  public static void Warning(FormattableString message)
  {
    if (IsEnabled(LogLevel.Warning)) Output!(LogLevel.Warning, message.ToString(null));
  }

  /// <inheritdoc cref="Log" />
  public static void Error(FormattableString message)
  {
    if (IsEnabled(LogLevel.Error)) Output!(LogLevel.Error, message.ToString(null));
  }

  /// <inheritdoc cref="Log" />
  public static void Critical(FormattableString message)
  {
    if (IsEnabled(LogLevel.Critical)) Output!(LogLevel.Critical, message.ToString(null));
  }

  #endregion

  #region 异常重载

  /// <summary>记录带异常的 Error 日志</summary>
  public static void Error(Exception ex, string message)
  {
    if (IsEnabled(LogLevel.Error))
      Output!(LogLevel.Error, $"{message}\n{ex}");
  }

  /// <summary>记录带异常的 Warning 日志</summary>
  public static void Warning(Exception ex, string message)
  {
    if (IsEnabled(LogLevel.Warning))
      Output!(LogLevel.Warning, $"{message}\n{ex}");
  }

  #endregion
}
