using System;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace SbModbus.Tool.Utils;

/// <summary>
///   ViewModel创建器
/// </summary>
public static class ViewModelLocator
{
  private static bool IsInDesignMode => Design.IsDesignMode;

  public static object? CreateViewModel(Type viewModelType)
  {
#if DEBUG
    if (IsInDesignMode) return Activator.CreateInstance(viewModelType);
#endif

    var t = Ioc.Default.GetService(viewModelType);
    return t ?? throw new NullReferenceException($"请先注入 {viewModelType.FullName}");
  }

  public static T CreateViewModel<T>() where T : class
  {
#if DEBUG
    if (IsInDesignMode) return Activator.CreateInstance<T>();
#endif

    var t = Ioc.Default.GetService<T>();
    return t ?? throw new NullReferenceException($"请先注入 {typeof(T).FullName}");
  }
}

public static class ViewModelLocatorExtensions
{
  extension(StyledElement view)
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

      if (viewModelType is not null) view.DataContext = ViewModelLocator.CreateViewModel(viewModelType);
    }

    public T? GetViewModel<T>() where T : class
    {
      return view.DataContext as T;
    }
  }
}