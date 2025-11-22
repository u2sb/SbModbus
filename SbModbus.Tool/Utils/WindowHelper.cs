using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace SbModbus.Tool.Utils;

public static class WindowHelper
{
  public static T CreateWindow<T>() where T : Window, new()
  {
    var window = Ioc.Default.GetService<T>();
    return window ?? new T();
  }

  public static bool IsInDesignMode()
  {
    return DesignerProperties.GetIsInDesignMode(new DependencyObject());
  }

  extension(FrameworkElement? view)
  {
    public bool IsInDesignMode()
    {
      return view is null
        ? IsInDesignMode()
        : DesignerProperties.GetIsInDesignMode(view);
    }
  }
}