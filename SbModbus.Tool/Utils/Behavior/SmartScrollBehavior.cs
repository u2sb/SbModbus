using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SbModbus.Tool.Utils.Behavior;

public static class SmartScrollBehavior
{
    #region 是否启用智能滚动
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), 
            typeof(SmartScrollBehavior), 
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj)
        => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value)
        => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            if ((bool)e.NewValue)
            {
                scrollViewer.ScrollChanged += OnScrollChanged;
                
                // 初始化状态
                SetLastExtentHeight(scrollViewer, scrollViewer.ExtentHeight);
                SetWasAtBottom(scrollViewer, IsAtBottom(scrollViewer));
            }
            else
            {
                scrollViewer.ScrollChanged -= OnScrollChanged;
            }
        }
    }
    #endregion

    #region 内部使用的附加属性
    private static readonly DependencyProperty LastExtentHeightProperty =
        DependencyProperty.RegisterAttached("LastExtentHeight", typeof(double), 
            typeof(SmartScrollBehavior));

    private static readonly DependencyProperty WasAtBottomProperty =
        DependencyProperty.RegisterAttached("WasAtBottom", typeof(bool), 
            typeof(SmartScrollBehavior));

    private static double GetLastExtentHeight(DependencyObject obj)
        => (double)obj.GetValue(LastExtentHeightProperty);
    
    private static void SetLastExtentHeight(DependencyObject obj, double value)
        => obj.SetValue(LastExtentHeightProperty, value);

    private static bool GetWasAtBottom(DependencyObject obj)
        => (bool)obj.GetValue(WasAtBottomProperty);
    
    private static void SetWasAtBottom(DependencyObject obj, bool value)
        => obj.SetValue(WasAtBottomProperty, value);
    #endregion

    #region 滚动变化处理
    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = (ScrollViewer)sender;
        
        double lastExtentHeight = GetLastExtentHeight(scrollViewer);
        bool wasAtBottom = GetWasAtBottom(scrollViewer);
        
        // 检查是否是内容高度增加
        if (e.ExtentHeightChange > 0 && Math.Abs(e.ExtentHeight - lastExtentHeight) > 0.1)
        {
            // 延迟执行以确保UI完全更新
            scrollViewer.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 如果之前是在底部，或者当前在底部，则滚动到底部
                if (wasAtBottom || IsAtBottom(scrollViewer))
                {
                    scrollViewer.ScrollToEnd();
                }
            }), DispatcherPriority.Background);
        }
        
        // 更新状态
        SetLastExtentHeight(scrollViewer, e.ExtentHeight);
        SetWasAtBottom(scrollViewer, IsAtBottom(scrollViewer, 5.0)); // 使用5像素容差
    }
    #endregion

    #region 辅助方法
    /// <summary>
    /// 判断是否在底部（考虑容差）
    /// </summary>
    /// <param name="scrollViewer">ScrollViewer控件</param>
    /// <param name="tolerance">容差像素数</param>
    /// <returns>是否在底部</returns>
    private static bool IsAtBottom(ScrollViewer scrollViewer, double tolerance = 10.0)
    {
        double verticalOffset = scrollViewer.VerticalOffset;
        double viewportHeight = scrollViewer.ViewportHeight;
        double extentHeight = scrollViewer.ExtentHeight;

        // 考虑容差，接近底部时也认为是底部
        return verticalOffset + viewportHeight + tolerance >= extentHeight;
    }
    #endregion
}