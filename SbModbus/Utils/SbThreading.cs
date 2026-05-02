using System.Threading;
using Microsoft.VisualStudio.Threading;

namespace SbModbus.Utils;

/// <summary>
///   Microsoft.VisualStudio.Threading 全局初始化入口。
/// </summary>
public static class SbThreading
{
  private static readonly object Sync = new();
  private static JoinableTaskContext? _context;

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
}

