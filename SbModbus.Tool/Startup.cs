using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SbModbus.Tool.Models.Settings;
using SbModbus.Tool.Services.RecordServices;
using SbModbus.Tool.ViewModels;
using SbModbus.Tool.Views.Windows;
using ZeroMessenger.DependencyInjection;
using MLogLevel = Microsoft.Extensions.Logging.LogLevel;
using SLogLevel = SbModbus.LogLevel;

namespace SbModbus.Tool;

// ReSharper disable once ClassNeverInstantiated.Global
public static class Startup
{
  private static IHost? _host;

  private static void ConfigLogging(HostApplicationBuilder builder)
  {
    var logger = builder.Logging;
    logger.ClearProviders();
  }

  /// <summary>
  ///   将 SbModbus 静态 Logger 桥接到 MEL（Microsoft.Extensions.Logging）
  /// </summary>
  private static void BridgeStaticLogger(IServiceProvider services)
  {
    var loggerFactory = services.GetRequiredService<ILoggerFactory>();
    var melLogger = loggerFactory.CreateLogger("SbModbus");

    SbModbus.Logger.Output = (level, message) =>
    {
      var melLevel = level switch
      {
        SLogLevel.Trace => MLogLevel.Trace,
        SLogLevel.Debug => MLogLevel.Debug,
        SLogLevel.Information => MLogLevel.Information,
        SLogLevel.Warning => MLogLevel.Warning,
        SLogLevel.Error => MLogLevel.Error,
        SLogLevel.Critical => MLogLevel.Critical,
        _ => MLogLevel.Information
      };
      melLogger.Log(melLevel, message);
    };

    // 从配置文件读取日志等级（如有），否则默认 Information
    SbModbus.Logger.Level = SLogLevel.Information;
  }

  private static void ConfigServices(HostApplicationBuilder builder)
  {
    var services = builder.Services;

    // 注入配置
    services.AddSingleton<AppSettings>(_ => AppSettings.Instance);
    services.AddZeroMessenger();

    // 传输记录器
    services.AddSingleton<DataTransmissionRecord>();

    services.AddViewsAndViewModels(typeof(Startup));
  }

  public static void OnStart(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
  {
    Dispatcher.UIThread.UnhandledException += App_DispatcherUnhandledException;
    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

    var builder = Host.CreateApplicationBuilder();

    ConfigLogging(builder);
    ConfigServices(builder);

    _host = builder.Build();
    Ioc.Default.ConfigureServices(_host.Services);
    AddLang();
    BridgeStaticLogger(_host.Services);
    _host.Start();

    if (sender is IClassicDesktopStyleApplicationLifetime desktop)
    {
      desktop.MainWindow = Ioc.Default.GetRequiredService<MainWindow>();
    }
  }

  private static void AddLang()
  {
    var appSettings = Design.IsDesignMode ? AppSettings.Instance : Ioc.Default.GetRequiredService<AppSettings>();

    var lang = appSettings.LanguageMap;
    var resources = Application.Current?.Resources;

    if (resources is not null)
      foreach (var (key, value) in lang)
      {
        var k = $"lang.{key}";
        resources[k] = value;
      }
  }

  public static void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
  {
    if (_host is null) return;

    using(_host)
    {
      _host.StopAsync().ConfigureAwait(true).GetAwaiter().GetResult();
    }
  }

  #region 异常

  private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
  {
    try
    {
      if (e.Exception is Exception exception) OnThrowExceptions(exception);
    }
    catch (Exception ex)
    {
      OnThrowExceptions(ex);
    }
    finally
    {
      e.SetObserved();
    }
  }

  private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
  {
    try
    {
      if (e.ExceptionObject is Exception exception) OnThrowExceptions(exception);
    }
    catch (Exception ex)
    {
      OnThrowExceptions(ex);
    }
  }

  private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
  {
    try
    {
      OnThrowExceptions(e.Exception);
    }
    catch (Exception ex)
    {
      OnThrowExceptions(ex);
    }
    finally
    {
      e.Handled = true;
    }
  }

  /// <summary>
  ///   抛出异常
  /// </summary>
  /// <param name="exception"></param>
  private static void OnThrowExceptions(Exception exception)
  {
    Console.WriteLine(exception.Message);
  }

  #endregion
}
