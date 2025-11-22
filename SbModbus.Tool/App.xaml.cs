using System.Windows;

namespace SbModbus.Tool;

/// <summary>
///   Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
  public new static App Current => (App)Application.Current;

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    SbModbus.Tool.Startup.OnStart(e.Args);
  }

  protected override void OnExit(ExitEventArgs e)
  {
    base.OnExit(e);
    SbModbus.Tool.Startup.OnExit(e.ApplicationExitCode);
  }
}