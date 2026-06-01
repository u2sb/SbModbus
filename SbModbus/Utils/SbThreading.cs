using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Utils;

/// <summary>
///   自实现的 JoinableTaskContext，替代 Microsoft.VisualStudio.Threading。
/// </summary>
public class JoinableTaskContext
{
  private readonly Thread? _mainThread;

  /// <summary>
  ///   无主线程引用的默认构造。
  /// </summary>
  public JoinableTaskContext()
  {
  }

  /// <summary>
  ///   显式指定主线程和同步上下文（SynchronizationContext 参数保留签名兼容，实际不使用）。
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
  /// </summary>
  public JoinableTaskFactory Factory => new(this);
}

/// <summary>
///   自实现的 JoinableTaskFactory，提供同步运行异步方法的能力。
///   通过临时清空 SynchronizationContext 避免主线程死锁，在当前线程上内联执行。
/// </summary>
public class JoinableTaskFactory
{
  private readonly JoinableTaskContext _context;

  internal JoinableTaskFactory(JoinableTaskContext context)
  {
    _context = context;
  }

  /// <summary>
  ///   同步执行异步方法并返回结果。
  ///   临时清空 SynchronizationContext 防止 await 捕获导致死锁，
  ///   异步任务在当前线程内联完成，不创建额外线程。
  /// </summary>
  public T Run<T>(Func<Task<T>> asyncFunc)
  {
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
  /// </summary>
  public void Run(Func<Task> asyncFunc)
  {
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
///   Microsoft.VisualStudio.Threading 全局初始化入口（自实现版本）。
/// </summary>
public static class SbThreading
{
  private static readonly object Sync = new();
  private static JoinableTaskContext? _context;

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
  ///   JoinableTaskFactory。
  /// </summary>
  public static JoinableTaskFactory Jtf => Context.Factory;

  /// <summary>
  ///   是否在初始化的主线程上。
  /// </summary>
  public static bool IsOnMainThread => Context.IsOnMainThread;

  /// <summary>
  ///   使用当前线程初始化主线程上下文。
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
}
