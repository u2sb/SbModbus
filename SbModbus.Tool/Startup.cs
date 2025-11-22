using System.IO;
using System.Reflection;
using System.Windows.Threading;
using SbModbus.Tool.Models.Settings;
using SbModbus.Tool.Services.RecordServices;
using SbModbus.Tool.Utils;
using SbModbus.Tool.ViewModels.Pages;
using SbModbus.Tool.ViewModels.Windows;
using SbModbus.Tool.Views.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using R3;
using ZeroMessenger.DependencyInjection;

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

  private static void ConfigServices(HostApplicationBuilder builder)
  {
    var services = builder.Services;

    // 注入配置
    services.AddSingleton<AppSettings>(_ => AppSettings.Instance);
    services.AddZeroMessenger();

    // 传输记录器
    services.AddSingleton<DataTransmissionRecord>();


    services.AddSingleton<MainWindowViewModel>(); // 主窗口
    services.AddSingleton<SerialPortAssistantPageViewModel>(); // 串口助手页面
    services.AddSingleton<ModbusPageViewModel>(); //Modbus界面
  }

  public static void OnStart(params string[] args)
  {
    App.Current.DispatcherUnhandledException += App_DispatcherUnhandledException;
    AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

    WpfProviderInitializer.SetDefaultObservableSystem(OnThrowExceptions);

    var builder = Host.CreateApplicationBuilder();

    ConfigLogging(builder);
    ConfigServices(builder);

    _host = builder.Build();
    Ioc.Default.ConfigureServices(_host.Services);
    AddLang();
    _host.Start();
    App.Current.MainWindow = new MainWindow();
    App.Current.MainWindow.Show();
  }

  private static void AddLang()
  {
    var isDesignMode = WindowHelper.IsInDesignMode();

    var appSettings = isDesignMode ? AppSettings.Instance : Ioc.Default.GetRequiredService<AppSettings>();

    var lang = appSettings.LanguageMap;
    var resources = App.Current.Resources;

#if DEBUG
    var assembly = Assembly.GetEntryAssembly();
    var attribute = assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
      .FirstOrDefault(attr => attr.Key == "ProjectRoot");
    var projectDir = attribute?.Value;
    if (Directory.Exists(projectDir))
    {
      var langFile = Path.Combine(projectDir, "Resources/Language.xaml");
      ResourceDictionaryGenerator.GenerateResourceDictionary(lang, langFile);
    }
#endif
    foreach (var (key, value) in lang)
    {
      var k = $"lang.{key}";
      resources[k] = value;
    }
  }

  public static void OnExit(int exitCode)
  {
    if (_host is null)
    {
      return;
    }

    using (_host)
    {
      _host.StopAsync().ConfigureAwait(true).GetAwaiter().GetResult();
    }
  }

  #region 异常

  private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
  {
    try
    {
      if (e.Exception is Exception exception)
      {
        OnThrowExceptions(exception);
      }
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
      if (e.ExceptionObject is Exception exception)
      {
        OnThrowExceptions(exception);
      }
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