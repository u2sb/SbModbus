using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Utils;

/// <summary>
///   自实现的 JoinableTaskContext，替代 Microsoft.VisualStudio.Threading。
///   跟踪主线程身份，供 <see cref="JoinableTaskFactory" /> 分派决策使用。
/// </summary>
public class JoinableTaskContext
{
  private readonly Thread? _mainThread;

  /// <summary>
  ///   无主线程引用的默认构造（非 UI 场景）。
  /// </summary>
  public JoinableTaskContext()
  {
  }

  /// <summary>
  ///   显式指定主线程和同步上下文。
  /// </summary>
  /// <param name="mainThread">主线程</param>
  /// <param name="synchronizationContext">主线程同步上下文（保留参数，内部未使用）</param>
  public JoinableTaskContext(Thread mainThread, SynchronizationContext? synchronizationContext)
  {
    _mainThread = mainThread;
  }

  /// <summary>
  ///   当前线程是否为初始化时指定的主线程。
  /// </summary>
  public bool IsOnMainThread => _mainThread is not null && Thread.CurrentThread == _mainThread;

  /// <summary>
  ///   获取关联的 JoinableTaskFactory。
  ///   需要先通过 <see cref="SbThreading" /> 初始化上下文和后台线程。
  /// </summary>
  public JoinableTaskFactory Factory => new(this, SbThreading.BackgroundThread);
}

/// <summary>
///   自实现的 JoinableTaskFactory，提供同步运行异步方法的能力。
///   在主线程上调用时自动将异步工作卸载到专用后台线程，避免阻塞 UI。
///   在非主线程上调用时内联执行（sync-over-async）。
/// </summary>
public class JoinableTaskFactory
{
  private readonly SbBackgroundThread _background;
  private readonly JoinableTaskContext _context;

  internal JoinableTaskFactory(JoinableTaskContext context, SbBackgroundThread background)
  {
    _context = context;
    _background = background;
  }

  /// <summary>
  ///   同步执行异步方法并返回结果。
  ///   主线程: 实际 IO 在后台线程执行，调用线程阻塞等待。
  ///   非主线程: 内联执行（sync-over-async），临时清空 SynchronizationContext。
  /// </summary>
  public T Run<T>(Func<Task<T>> asyncFunc)
  {
    // 场景1: 在主线程上调用，且后台线程已就绪 — 卸载到后台线程执行
    if (_context.IsOnMainThread && _background.IsRunning)
      return _background.Run(asyncFunc);

    // 场景2: 非主线程（或后台线程未就绪时的降级） — 内联执行
    var prevContext = SynchronizationContext.Current;
    try
    {
      SynchronizationContext.SetSynchronizationContext(null);
      return asyncFunc().GetAwaiter().GetResult();
    }
    finally
    {
      SynchronizationContext.SetSynchronizationContext(prevContext);
    }
  }

  /// <summary>
  ///   同步执行异步方法（无返回值）。
  ///   主线程: 实际 IO 在后台线程执行，调用线程阻塞等待。
  ///   非主线程: 内联执行（sync-over-async），临时清空 SynchronizationContext。
  /// </summary>
  public void Run(Func<Task> asyncFunc)
  {
    // 场景1: 在主线程上调用 — 卸载到后台线程
    if (_context.IsOnMainThread && _background.IsRunning)
    {
      _background.Run(asyncFunc);
      return;
    }

    // 场景2: 非主线程 — 内联执行
    var prevContext = SynchronizationContext.Current;
    try
    {
      SynchronizationContext.SetSynchronizationContext(null);
      asyncFunc().GetAwaiter().GetResult();
    }
    finally
    {
      SynchronizationContext.SetSynchronizationContext(prevContext);
    }
  }
}

/// <summary>
///   SbModbus 线程基础设施 — 全局入口。
///   提供 JoinableTaskFactory、后台工作线程和生命周期管理。
/// </summary>
/// <remarks>
///   <para>推荐初始化（WPF/Avalonia 场景）:</para>
///   <code>
///     SbThreading.InitializeMainThread();  // App 构造函数中调用
///   </code>
///   <para>清理（应用退出前）:</para>
///   <code>
///     SbThreading.Shutdown();
///   </code>
/// </remarks>
public static class SbThreading
{
  private static readonly object Sync = new();
  private static JoinableTaskContext? _context;
  private static SbBackgroundThread? _backgroundThread;

  /// <summary>
  ///   线程上下文。
  /// </summary>
  public static JoinableTaskContext Context
  {
    get
    {
      lock (Sync)
      {
        return _context ??= new JoinableTaskContext();
      }
    }
  }

  /// <summary>
  ///   JoinableTaskFactory — 提供 <c>Run(Func{Task})</c> 和 <c>Run{T}(Func{Task{T}})</c>。
  ///   主线程上调用自动卸载到后台线程。
  /// </summary>
  public static JoinableTaskFactory Jtf => Context.Factory;

  /// <summary>
  ///   是否在初始化的主线程上。
  /// </summary>
  public static bool IsOnMainThread => Context.IsOnMainThread;

  /// <summary>
  ///   专用后台工作线程（懒加载单例）。
  /// </summary>
  internal static SbBackgroundThread BackgroundThread
  {
    get
    {
      if (_backgroundThread is { IsRunning: true }) return _backgroundThread;

      lock (Sync)
      {
        if (_backgroundThread is { IsRunning: true }) return _backgroundThread;

        _backgroundThread = new SbBackgroundThread();

        // 转发未处理异常事件
        _backgroundThread.OnUnhandledException += ex => OnUnhandledException?.Invoke(ex);

        _backgroundThread.Start();
        return _backgroundThread;
      }
    }
  }

  /// <summary>
  ///   监听后台线程未处理异常（可选）。
  ///   异常已被 Logger.Error 记录，此事件仅用于自定义处理。
  /// </summary>
  public static event Action<Exception>? OnUnhandledException;

  /// <summary>
  ///   使用当前线程初始化主线程上下文（WPF/Avalonia 的 App 构造函数中调用）。
  /// </summary>
  public static void InitializeMainThread()
  {
    InitializeMainThread(Thread.CurrentThread, SynchronizationContext.Current ?? new SynchronizationContext());
  }

  /// <summary>
  ///   显式初始化主线程上下文。
  /// </summary>
  /// <param name="mainThread">主线程。</param>
  /// <param name="synchronizationContext">主线程同步上下文。</param>
  public static void InitializeMainThread(Thread mainThread, SynchronizationContext? synchronizationContext)
  {
    lock (Sync)
    {
      _context = new JoinableTaskContext(mainThread, synchronizationContext ?? new SynchronizationContext());
    }
  }

  /// <summary>
  ///   优雅关闭后台线程。应用退出前调用。
  /// </summary>
  /// <param name="timeout">最大等待时间，默认 5 秒。</param>
  public static void Shutdown(TimeSpan? timeout = null)
  {
    SbBackgroundThread? thread;
    lock (Sync)
    {
      thread = _backgroundThread;
      _backgroundThread = null;
    }

    if (thread == null) return;

    thread.OnUnhandledException -= _ => { };
    thread.Stop(timeout);
    thread.Dispose();
  }
}
