using Avalonia.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using SbModbus.Tool.ViewModels;

namespace SbModbus.Tool.Utils;

/// <summary>
///   ViewModel创建器
/// </summary>
public static class ViewModelLocator
{
  public static object? CreateViewModel(Type viewModelType, bool isInDesignMode = false)
  {
#if DEBUG
    if (isInDesignMode) return Activator.CreateInstance(viewModelType);
#endif

    var t = Ioc.Default.GetService(viewModelType);
    return t ?? throw new NullReferenceException($"请先注入 {viewModelType.FullName}");
  }
}

public static class ViewModelLocatorExtensions
{
  extension(Control view)
  {
    public void AutoCreateViewModel()
    {
      var viewType = view.GetType();

      // 先替换路径
      var viewModelTypeName = viewType.FullName?.Replace(".Views.", ".ViewModels.");

      if (viewModelTypeName is null) return;

      // 再替换文件名
      viewModelTypeName = viewModelTypeName.EndsWith("View")
        ? viewModelTypeName.Remove(viewModelTypeName.Length - 4) + "ViewModel"
        : viewModelTypeName + "ViewModel";

      var viewModelType = Type.GetType(viewModelTypeName);

      if (viewModelType is not null)
      {
        view.DataContext = ViewModelLocator.CreateViewModel(viewModelType, view.IsInDesignMode);
        if (view.DataContext is ViewModelBase viewModelBase)
        {
          viewModelBase.View = view;

          view.Loaded -= viewModelBase.OnLoaded;
          view.Loaded += viewModelBase.OnLoaded;

          view.Unloaded -= viewModelBase.OnUnloaded;
          view.Unloaded += viewModelBase.OnUnloaded;

          view.Initialized -= viewModelBase.OnInitialized;
          view.Initialized += viewModelBase.OnInitialized;
        }
      }
    }

    public T? GetViewModel<T>() where T : class
    {
      return view.DataContext as T;
    }
  }
}
