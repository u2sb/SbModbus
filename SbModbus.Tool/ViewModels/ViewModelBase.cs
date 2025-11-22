using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SbModbus.Tool.ViewModels;

public class ViewModelBase : ObservableValidator
{
  public bool IsDesignMode { get; set; } = false;

  public FrameworkElement? View { get; set; }

  public Window? Window
  {
    get
    {
      if (View is null)
      {
        return null;
      }

      if (View is Window window)
      {
        return window;
      }

      if (View.IsLoaded)
      {
        return Window.GetWindow(View);
      }

      DependencyObject? parent = View;
      while (parent != null)
      {
        if (parent is Window w)
        {
          return w;
        }

        parent = VisualTreeHelper.GetParent(parent) ?? (parent as FrameworkElement)?.Parent;
      }

      return null;
    }
  }

  public virtual void OnLoaded(object sender, RoutedEventArgs e)
  {
  }

  public virtual void OnUnloaded(object sender, RoutedEventArgs e)
  {
  }

  public virtual void IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
  {
  }
}