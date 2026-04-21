using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SbModbus.Tool.ViewModels.Windows;
using SbModbus.Tool.Views.Windows;

namespace SbModbus.Tool;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public class App : Application
{
    public new static Application Current { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Current = this;
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
