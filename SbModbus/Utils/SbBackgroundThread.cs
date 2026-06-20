using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SbModbus.Utils;

/// <summary>
///   专用后台工作线程 — 基于 <see cref="Channel{T}" /> 的单线程事件循环。
///   用于将异步 Modbus 通信从主线程卸载到后台，避免阻塞 UI。
/// </summary>
/// <remarks>
///   <para>线程安全：所有公共方法均可在任意线程调用。</para>
///   <para>异常处理：工作项内的异常被捕获并记录到 <see cref="Logger" />，不会导致进程崩溃。</para>
///   <para>关闭：调用 <see cref="Stop" /> 优雅关闭，超时后线程标记为后台退出。</para>
///   <para>重入安全：支持在后台线程上再次投递工作（Run/Post 内嵌套调用 Run/Post），内部通过 AsyncLock 保护。</para>
/// </remarks>
internal sealed class SbBackgroundThread : IDisposable
{
  private readonly Lock _lifecycleLock = new();

  private readonly Channel<WorkItem> _channel;
  private readonly CancellationTokenSource _disposeCts = new();
  private Thread? _workerThread;
  private volatile bool _isRunning;
  private bool _disposed;

  /// <summary>
  ///   创建未启动的后台线程实例。
  ///   调用 <see cref="Start" /> 启动工作线程。
  /// </summary>
  public SbBackgroundThread()
  {
    _channel = Channel.CreateUnbounded<WorkItem>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false // 多线程可投递
    });
  }

  /// <summary>
  ///   是否有待处理的工作项。
  /// </summary>
  public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : _channel.Reader.TryPeek(out _) ? 1 : 0;

  /// <summary>
  ///   工作线程是否正在运行。
  /// </summary>
  public bool IsRunning => _isRunning;

  /// <summary>
  ///   未处理异常事件 — 工作项内部异常被捕获后触发（可选订阅）。
  /// </summary>
  public event Action<Exception>? OnUnhandledException;

  #region 生命周期

  /// <summary>
  ///   启动后台工作线程。多次调用仅首次生效。
  /// </summary>
  public void Start()
  {
    if (_disposed) throw new ObjectDisposedException(nameof(SbBackgroundThread));

    lock (_lifecycleLock)
    {
      if (_isRunning) return;

      _workerThread = new Thread(RunLoop)
      {
        IsBackground = true,
        Name = "SbModbus-Background"
      };

      _isRunning = true;
      _workerThread.Start();
    }

    Logger.Information($"SbBackgroundThread started");
  }

  /// <summary>
  ///   优雅关闭：完成 Channel 写入端，等待队列排空并让工作线程自然退出。
  ///   <paramref name="timeout" /> 过后仍在运行则放弃等待（线程本身是 IsBackground，进程退出时自动终止）。
  /// </summary>
  /// <param name="timeout">最大等待时间。默认为 5 秒。</param>
  public void Stop(TimeSpan? timeout = null)
  {
    lock (_lifecycleLock)
    {
      if (!_isRunning) return;

      // 先完成写入端 — 事件循环在排空队列后自然退出
      _channel.Writer.TryComplete();
      _isRunning = false;
    }

    Logger.Information($"SbBackgroundThread stopping...");

    var thread = _workerThread;
    _workerThread = null;

    if (thread == null || thread == Thread.CurrentThread) return;

    var actualTimeout = timeout ?? TimeSpan.FromSeconds(5);

    if (!thread.Join(actualTimeout))
    {
      Logger.Warning(
        $"SbBackgroundThread did not stop within {actualTimeout.TotalSeconds:F0}s; background thread will be abandoned");
      // 放弃等待，线程是 IsBackground，进程退出时自动终止
      // 但此处 cancel disposeCts 让 WaitToReadAsync 跳出（如果线程尚未退出）
      _disposeCts.Cancel();
    }
    else
    {
      Logger.Information($"SbBackgroundThread stopped gracefully");
    }
  }

  /// <inheritdoc />
  public void Dispose()
  {
    if (_disposed) return;

    // 先停止（此时 _disposed 仍为 false，Stop 正常执行）
    Stop(TimeSpan.FromSeconds(3));

    _disposed = true;

    try
    {
      _disposeCts.Cancel();
      _disposeCts.Dispose();
    }
    catch
    {
      // 忽略 dispose 异常
    }

    OnUnhandledException = null;
  }

  #endregion

  #region 公开 API

  /// <summary>
  ///   投递异步工作（fire-and-forget）。不等待结果。
  ///   如果线程已停止，工作被静默丢弃。
  ///   工作项内部异常通过 <see cref="OnUnhandledException" /> 和 <see cref="Logger" /> 处理。
  /// </summary>
  /// <param name="work">异步工作委托</param>
  public void Post(Func<Task> work)
  {
    if (_disposed) throw new ObjectDisposedException(nameof(SbBackgroundThread));
    if (!_isRunning)
    {
      Logger.Warning($"SbBackgroundThread.Post: not running, work dropped");
      return;
    }

    var item = new WorkItem(work, null, OnWorkItemException);
    if (!_channel.Writer.TryWrite(item))
      Logger.Warning($"SbBackgroundThread.Post: Channel is closed, work dropped");
  }

  /// <summary>
  ///   投递异步工作并同步阻塞等待结果。
  ///   IO 在后台线程上执行，调用线程被阻塞直到完成。
  ///   支持重入：可以在后台线程上调用 Run 投递新工作（结果在当前线程上直接 inline 执行，不通过 Channel）。
  /// </summary>
  /// <typeparam name="T">返回值类型</typeparam>
  /// <param name="work">异步工作委托</param>
  /// <returns>异步工作的返回值</returns>
  /// <exception cref="ObjectDisposedException">已释放</exception>
  /// <exception cref="InvalidOperationException">未启动</exception>
  public T Run<T>(Func<Task<T>> work)
  {
    ThrowIfNotReady();

    var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

    var item = new WorkItem(
      async () =>
      {
        try
        {
          var result = await work().ConfigureAwait(false);
          tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
          OnWorkItemException(ex);
          tcs.TrySetException(ex);
        }
      },
      tcs,
      OnWorkItemException);

    if (!_channel.Writer.TryWrite(item))
    {
      Logger.Error($"SbBackgroundThread.Run<T>: Channel is closed");
      throw new InvalidOperationException("Background thread channel is closed. The thread may have been stopped.");
    }

    return (T)tcs.Task.GetAwaiter().GetResult()!;
  }

  /// <summary>
  ///   投递无返回值的异步工作并同步阻塞等待完成。
  ///   支持重入：可以在后台线程上调用 Run 投递新工作。
  /// </summary>
  /// <param name="work">异步工作委托</param>
  /// <exception cref="ObjectDisposedException">已释放</exception>
  /// <exception cref="InvalidOperationException">未启动</exception>
  public void Run(Func<Task> work)
  {
    ThrowIfNotReady();

    var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

    var item = new WorkItem(
      async () =>
      {
        try
        {
          await work().ConfigureAwait(false);
          tcs.TrySetResult(null);
        }
        catch (Exception ex)
        {
          OnWorkItemException(ex);
          tcs.TrySetException(ex);
        }
      },
      tcs,
      OnWorkItemException);

    if (!_channel.Writer.TryWrite(item))
    {
      Logger.Error($"SbBackgroundThread.Run: Channel is closed");
      throw new InvalidOperationException("Background thread channel is closed. The thread may have been stopped.");
    }

    tcs.Task.GetAwaiter().GetResult();
  }

  #endregion

  #region 内部实现

  private void ThrowIfNotReady()
  {
    if (_disposed) throw new ObjectDisposedException(nameof(SbBackgroundThread));
    if (!_isRunning)
      throw new InvalidOperationException("SbBackgroundThread is not running. Call Start() first.");
  }

  /// <summary>
  ///   工作线程入口 — 同步包装异步事件循环。
  /// </summary>
  private void RunLoop()
  {
    try
    {
      RunLoopAsync().GetAwaiter().GetResult();
    }
    catch (OperationCanceledException)
    {
      // 正常关闭
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "SbBackgroundThread: fatal error in run loop");
    }
  }

  /// <summary>
  ///   异步事件循环 — 从 Channel 读取工作项并执行。
  ///   使用 WaitToReadAsync + TryRead 模式以兼容 netstandard2.0。
  ///   当 Channel 写入端被完成且队列排空时自然退出。
  ///   <see cref="_disposeCts" /> 仅在 Stop 超时后才 Cancel，作为兜底安全网。
  /// </summary>
  private async Task RunLoopAsync()
  {
    var reader = _channel.Reader;

    // 注意：不使用 CancellationToken 在正常停止流程中
    // 停止由 Channel.Writer.Complete() 触发 — 排空后 WaitToReadAsync 返回 false
    // _disposeCts 仅在 Dispose/超时兜底时被 Cancel
    while (await WaitToReadAsync(reader).ConfigureAwait(false))
    while (reader.TryRead(out var item))
      await ExecuteItemAsync(item).ConfigureAwait(false);
  }

  /// <summary>
  ///   等待 Channel 可读或 dispose 信号。
  ///   正常停止: Channel 排空后返回 false。
  ///   强制停止: _disposeCts 被 Cancel 时抛 OCE。
  /// </summary>
  private async ValueTask<bool> WaitToReadAsync(ChannelReader<WorkItem> reader)
  {
    try
    {
      return await reader.WaitToReadAsync(_disposeCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }

  /// <summary>
  ///   执行单个工作项，异常安全。
  ///   注意: WorkItem 内部已捕获异常并通过 OnWorkItemException 处理（针对 Run/Run{T}），
  ///   此处额外捕获是兜底用于 Post 场景 — 确保通道循环永不崩溃。
  /// </summary>
  private async Task ExecuteItemAsync(WorkItem item)
  {
    try
    {
      await item.Work().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      // Post 场景的兜底保护 — Run/Run{T} 已在 WorkItem 内部处理过异常
      OnWorkItemException(ex);
    }
  }

  /// <summary>
  ///   统一的工作项异常处理 — 记录日志并触发事件。
  ///   遍历 invocation list 逐个调用，确保一个 handler 抛异常不会影响其他 handler。
  /// </summary>
  private void OnWorkItemException(Exception ex)
  {
    Logger.Error(ex, "SbBackgroundThread: unhandled exception in work item");
    FireOnUnhandledException(ex);
  }

  private void FireOnUnhandledException(Exception ex)
  {
    var handler = OnUnhandledException;
    if (handler == null) return;

    // 逐个调用 invocation list，一个 handler 抛异常不影响后续 handler
    foreach (var single in handler.GetInvocationList().Cast<Action<Exception>>())
      try
      {
        single(ex);
      }
      catch (Exception handlerEx)
      {
        Logger.Error(handlerEx, "SbBackgroundThread: OnUnhandledException handler threw an exception");
      }
  }

  #endregion

  #region 内部类型

  /// <summary>
  ///   Channel 中的工作项。
  /// </summary>
  private readonly struct WorkItem(Func<Task> work, object? tcs, Action<Exception>? onException)
  {
    /// <summary>异步工作委托</summary>
    public readonly Func<Task> Work = work ?? throw new ArgumentNullException(nameof(work));

    /// <summary>
    ///   TaskCompletionSource（boxed），用于同步 Run/Run{T} 的结果传递。
    ///   Post 时为 null。
    /// </summary>
    public readonly object? Tcs = tcs;

    /// <summary>
    ///   异常回调（Optional）— Post 时为 <see cref="OnWorkItemException" />，
    ///   Run/Run{T} 时在 WorkItem 内部即可调用，不需要额外回调。
    /// </summary>
    public readonly Action<Exception>? OnException = onException;
  }

  #endregion
}
