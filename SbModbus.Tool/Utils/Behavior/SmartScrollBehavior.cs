using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SbModbus.Tool.Utils.Behavior;

public static class SmartScrollBehavior
{
  #region 滚动变化处理

  private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
  {
    var scrollViewer = (ScrollViewer)sender!;

    var lastExtentHeight = GetLastExtentHeight(scrollViewer);
    var wasAtBottom = GetWasAtBottom(scrollViewer);

    var extentHeightChange = scrollViewer.Extent.Height - lastExtentHeight;

    // 检查是否是内容高度增加
    if (extentHeightChange > 0 && Math.Abs(extentHeightChange) > 0.1)
      // 延迟执行以确保UI完全更新
      Dispatcher.UIThread.InvokeAsync(() =>
      {
        // 如果之前是在底部，或者当前在底部，则滚动到底部
        if (wasAtBottom || IsAtBottom(scrollViewer)) scrollViewer.ScrollToEnd();
      }, DispatcherPriority.Background);

    // 更新状态
    SetLastExtentHeight(scrollViewer, scrollViewer.Extent.Height);
    SetWasAtBottom(scrollViewer, IsAtBottom(scrollViewer, 5.0)); // 使用5像素容差
  }

  #endregion

  #region 辅助方法

  /// <summary>
  ///   判断是否在底部（考虑容差）
  /// </summary>
  /// <param name="scrollViewer">ScrollViewer控件</param>
  /// <param name="tolerance">容差像素数</param>
  /// <returns>是否在底部</returns>
  private static bool IsAtBottom(ScrollViewer scrollViewer, double tolerance = 10.0)
  {
    var verticalOffset = scrollViewer.Offset.Y;
    var viewportHeight = scrollViewer.Viewport.Height;
    var extentHeight = scrollViewer.Extent.Height;

    // 考虑容差，接近底部时也认为是底部
    return verticalOffset + viewportHeight + tolerance >= extentHeight;
  }

  #endregion

  #region 是否启用智能滚动

  public static readonly AttachedProperty<bool> IsEnabledProperty =
    AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
      "IsEnabled", typeof(SmartScrollBehavior));

  public static bool GetIsEnabled(ScrollViewer obj)
  {
    return obj.GetValue(IsEnabledProperty);
  }

  public static void SetIsEnabled(ScrollViewer obj, bool value)
  {
    obj.SetValue(IsEnabledProperty, value);
  }

  static SmartScrollBehavior()
  {
    IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnIsEnabledChanged);
  }

  private static void OnIsEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
  {
    if ((bool)e.NewValue!)
    {
      scrollViewer.ScrollChanged += OnScrollChanged;

      // 初始化状态
      SetLastExtentHeight(scrollViewer, scrollViewer.Extent.Height);
      SetWasAtBottom(scrollViewer, IsAtBottom(scrollViewer));
    }
    else
    {
      scrollViewer.ScrollChanged -= OnScrollChanged;
    }
  }

  #endregion

  #region 内部使用的附加属性

  private static readonly AttachedProperty<double> LastExtentHeightProperty =
    AvaloniaProperty.RegisterAttached<ScrollViewer, double>(
      "LastExtentHeight", typeof(SmartScrollBehavior));

  private static readonly AttachedProperty<bool> WasAtBottomProperty =
    AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
      "WasAtBottom", typeof(SmartScrollBehavior));

  private static double GetLastExtentHeight(ScrollViewer obj)
  {
    return obj.GetValue(LastExtentHeightProperty);
  }

  private static void SetLastExtentHeight(ScrollViewer obj, double value)
  {
    obj.SetValue(LastExtentHeightProperty, value);
  }

  private static bool GetWasAtBottom(ScrollViewer obj)
  {
    return obj.GetValue(WasAtBottomProperty);
  }

  private static void SetWasAtBottom(ScrollViewer obj, bool value)
  {
    obj.SetValue(WasAtBottomProperty, value);
  }

  #endregion
}
