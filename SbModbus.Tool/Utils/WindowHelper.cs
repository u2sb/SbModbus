using Avalonia.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace SbModbus.Tool.Utils;

public static class WindowHelper
{
  public static T CreateWindow<T>() where T : Window, new()
  {
    var window = Ioc.Default.GetService<T>();
    return window ?? new T();
  }
}