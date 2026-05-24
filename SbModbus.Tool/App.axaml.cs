using System.Globalization;
using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SbModbus.Utils;

namespace SbModbus.Tool;

/// <summary>
///   Interaction logic for App.xaml
/// </summary>
public class App : Application
{
  public App()
  {
    Current = this;
    SbThreading.InitializeMainThread();
  }

  public new static Application Current { get; private set; } = null!;

  public override void Initialize()
  {
    AvaloniaXamlLoader.Load(this);
    this.UseAtomUI(builder =>
    {
      builder.WithDefaultCultureInfo(CultureInfo.CurrentUICulture);
      builder.WithDefaultTheme(IThemeManager.DEFAULT_THEME_ID);
      builder.UseAlibabaSansFont();
      builder.UseDesktopControls();
    });
  }

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IControlledApplicationLifetime lifetime)
    {
      lifetime.Startup -= Startup.OnStart;
      lifetime.Startup += Startup.OnStart;

      lifetime.Exit -= Startup.OnExit;
      lifetime.Exit += Startup.OnExit;
    }

    base.OnFrameworkInitializationCompleted();
  }
}
