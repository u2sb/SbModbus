using System.Windows;
using SbModbus.Tool.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace SbModbus.Tool.Utils;

/// <summary>
///   ViewModel创建器
/// </summary>
public static class ViewModelLocator
{
  public static object? CreateViewModel(Type viewModelType, bool isInDesignMode = false)
  {
#if DEBUG
    if (isInDesignMode)
    {
      return Activator.CreateInstance(viewModelType);
    }
#endif

    var t = Ioc.Default.GetService(viewModelType);
    return t ?? throw new NullReferenceException($"请先注入 {viewModelType.FullName}");
  }

  public static T CreateViewModel<T>(bool isInDesignMode = false) where T : class
  {
#if DEBUG
    if (isInDesignMode)
    {
      return Activator.CreateInstance<T>();
    }
#endif

    var t = Ioc.Default.GetService<T>();
    return t ?? throw new NullReferenceException($"请先注入 {typeof(T).FullName}");
  }
}

public static class ViewModelLocatorExtensions
{
  extension(FrameworkElement view)
  {
    public void AutoCreateViewModel()
    {
      var viewType = view.GetType();

      // 先替换路径
      var viewModelTypeName = viewType.FullName?.Replace(".Views.", ".ViewModels.");

      if (viewModelTypeName is null)
      {
        return;
      }

      // 再替换文件名
      viewModelTypeName = viewModelTypeName.EndsWith("View")
        ? viewModelTypeName.Remove(viewModelTypeName.Length - 4) + "ViewModel"
        : viewModelTypeName + "ViewModel";

      var viewModelType = Type.GetType(viewModelTypeName);

      if (viewModelType is not null)
      {
#if DEBUG
        var isInDesignMode = view.IsInDesignMode();
        view.DataContext = ViewModelLocator.CreateViewModel(viewModelType, isInDesignMode);
        if (view.DataContext is ViewModelBase vmb)
        {
          vmb.IsDesignMode = isInDesignMode;
        }
#else
        view.DataContext = ViewModelLocator.CreateViewModel(viewModelType);
#endif
        if (view.DataContext is ViewModelBase viewModelBase)
        {
          viewModelBase.View = view;

          view.Loaded += viewModelBase.OnLoaded;
          view.Unloaded += viewModelBase.OnUnloaded;
          view.IsVisibleChanged += viewModelBase.IsVisibleChanged;
        }
      }
    }

    public T? GetViewModel<T>() where T : class
    {
      return view.DataContext as T;
    }
  }
}