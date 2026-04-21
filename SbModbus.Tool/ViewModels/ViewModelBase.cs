using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace SbModbus.Tool.ViewModels;

public class ViewModelBase : ObservableValidator
{
  public static bool IsDesignMode { get; set; } = Design.IsDesignMode;

  public Control? View { get; set; }

  public Window? Window
  {
    get
    {
      if (View is null) return null;

      if (View is Window window) return window;

      if (View.IsLoaded) return TopLevel.GetTopLevel(View) as Window;

      return null;
    }
  }

  protected T? GetService<T>(T? defaultValue = null) where T : class
  {
    if (!IsDesignMode)
    {
      return Ioc.Default.GetService<T>() ?? defaultValue;
    }

    return defaultValue;
  }


  public virtual void OnLoaded(object? sender, RoutedEventArgs e)
  {
  }

  public virtual void OnUnloaded(object? sender, RoutedEventArgs e)
  {
  }

  public virtual void OnInitialized(object? sender, EventArgs e)
  {
  }
}
