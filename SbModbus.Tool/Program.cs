using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Vulkan;

namespace SbModbus.Tool;

internal static class Program
{
  // Initialization code. Don't use any Avalonia, third-party APIs or any
  // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
  // yet and stuff might break.
  [STAThread]
  public static int Main(string[] args)
  {
    var builder = BuildAvaloniaApp();

    return builder.StartWithClassicDesktopLifetime(args,
      lifetimeBuilder => { lifetimeBuilder.ShutdownMode = ShutdownMode.OnLastWindowClose; });
  }

  // Avalonia configuration, don't remove; also used by visual designer.
  private static AppBuilder BuildAvaloniaApp()
  {
    var builder = AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .WithInterFont()
      .LogToTrace()
      .UseR3();

    if (OperatingSystem.IsWindows())
    {
      builder.With(new Win32PlatformOptions
      {
        RenderingMode =
          [Win32RenderingMode.Vulkan, Win32RenderingMode.AngleEgl, Win32RenderingMode.Wgl, Win32RenderingMode.Software],
        
      });
    }

    return builder;
  }
}