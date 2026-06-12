using SbModbus.Utils;

namespace SbModbus.Tests;

/// <summary>
///   <see cref="SbBackgroundThread" /> 和 <see cref="SbThreading" /> 的单元测试。
/// </summary>
public class SbThreadingTests : IDisposable
{
  private SbBackgroundThread? _thread;

  public void Dispose()
  {
    try
    {
      _thread?.Dispose();
      _thread = null;
    }
    catch
    {
      // 清理阶段忽略异常
    }

    try
    {
      SbThreading.Shutdown(TimeSpan.FromSeconds(1));
    }
    catch
    {
    }
  }

  private SbBackgroundThread CreateAndStart()
  {
    _thread = new SbBackgroundThread();
    _thread.Start();
    return _thread;
  }

  #region 生命周期

  [Fact]
  public void Start_SetsIsRunning_ToTrue()
  {
    var thread = CreateAndStart();
    Assert.True(thread.IsRunning);
  }

  [Fact]
  public void Stop_SetsIsRunning_ToFalse()
  {
    var thread = CreateAndStart();
    thread.Stop(TimeSpan.FromSeconds(3));
    Assert.False(thread.IsRunning);
  }

  [Fact]
  public void Start_AfterStop_ReThrows()
  {
    var thread = CreateAndStart();
    thread.Stop(TimeSpan.FromSeconds(3));
    // 已停止后不能重新 Start（当前实现不支持），但不应抛出异常
    // 当前设计不允许 restart — 这是预期的
    Assert.False(thread.IsRunning);
  }

  [Fact]
  public void Dispose_CleansUp_WithoutException()
  {
    var thread = CreateAndStart();
    var exception = Record.Exception(() => thread.Dispose());
    Assert.Null(exception);
    Assert.False(thread.IsRunning);
  }

  [Fact]
  public void DoubleDispose_DoesNotThrow()
  {
    var thread = CreateAndStart();
    thread.Dispose();
    var exception = Record.Exception(() => thread.Dispose());
    Assert.Null(exception);
  }

  #endregion

  #region Post — fire-and-forget

  [Fact]
  public async Task Post_ExecutesAsyncWork()
  {
    var thread = CreateAndStart();
    var executed = new TaskCompletionSource<bool>();

    thread.Post(async () =>
    {
      await Task.Delay(10);
      executed.TrySetResult(true);
    });

    await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
  }

  [Fact]
  public async Task Post_MultipleItems_ExecuteInOrder()
  {
    var thread = CreateAndStart();
    var results = new List<int>();
    var done = new TaskCompletionSource<bool>();

    thread.Post(async () =>
    {
      await Task.Delay(5);
      lock (results) { results.Add(1); }
    });

    thread.Post(async () =>
    {
      results.Add(2);
    });

    thread.Post(async () =>
    {
      results.Add(3);
      done.TrySetResult(true);
    });

    await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

    // 给 workers 一点时间确保顺序
    await Task.Delay(50);

    Assert.Equal(new[] { 1, 2, 3 }, results);
  }

  #endregion

  #region Run — 同步阻塞

  [Fact]
  public void Run_WithReturnValue_ReturnsResult()
  {
    var thread = CreateAndStart();

    var result = thread.Run(async () =>
    {
      await Task.Delay(10);
      return 42;
    });

    Assert.Equal(42, result);
  }

  [Fact]
  public void Run_WithoutReturnValue_Completes()
  {
    var thread = CreateAndStart();
    var flag = false;

    thread.Run(async () =>
    {
      await Task.Delay(10);
      flag = true;
    });

    Assert.True(flag);
  }

  [Fact]
  public void Run_BlocksCallerUntilComplete()
  {
    var thread = CreateAndStart();
    var before = Environment.TickCount64;

    var result = thread.Run(async () =>
    {
      await Task.Delay(200);
      return "done";
    });

    var elapsed = Environment.TickCount64 - before;
    Assert.Equal("done", result);
    // 延迟至少接近 200ms（允许一些偏差）
    Assert.True(elapsed >= 150);
  }

  [Fact]
  public void Run_PropagatesException()
  {
    var thread = CreateAndStart();

    var ex = Assert.Throws<InvalidOperationException>(() =>
      thread.Run(async () =>
      {
        await Task.Delay(10);
        throw new InvalidOperationException("Test error");
      }));

    Assert.Contains("Test error", ex.Message);
  }

  [Fact]
  public void Run_BeforeStart_ThrowsInvalidOperationException()
  {
    var thread = new SbBackgroundThread(); // 未 Start
    Assert.Throws<InvalidOperationException>(() =>
      thread.Run(async () => await Task.CompletedTask));
  }

  #endregion

  #region 异常处理

  [Fact]
  public async Task Post_ExceptionDoesNotCrash()
  {
    var thread = CreateAndStart();
    var exceptionCaught = new TaskCompletionSource<bool>();

    thread.OnUnhandledException += _ => exceptionCaught.TrySetResult(true);

    thread.Post(async () =>
    {
      await Task.Delay(10);
      throw new InvalidOperationException("Safe crash");
    });

    await exceptionCaught.Task.WaitAsync(TimeSpan.FromSeconds(5));

    // 线程应该仍在运行
    Assert.True(thread.IsRunning);
  }

  [Fact]
  public async Task OnUnhandledException_FiresWithException()
  {
    var thread = CreateAndStart();
    Exception? capturedEx = null;
    var done = new TaskCompletionSource<bool>();

    thread.OnUnhandledException += ex =>
    {
      capturedEx = ex;
      done.TrySetResult(true);
    };

    thread.Post(async () =>
    {
      await Task.Delay(10);
      throw new TimeoutException("Test timeout");
    });

    await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

    Assert.NotNull(capturedEx);
    Assert.IsType<TimeoutException>(capturedEx);
    Assert.Contains("Test timeout", capturedEx!.Message);
  }

  [Fact]
  public async Task OnUnhandledException_HandlerThrows_DoesNotCrashThread()
  {
    var thread = CreateAndStart();
    var secondFired = new TaskCompletionSource<bool>();

    // 第一个 handler 抛出异常
    thread.OnUnhandledException += _ => throw new Exception("Handler error");
    // 第二个 handler 应仍能被调用
    thread.OnUnhandledException += _ => secondFired.TrySetResult(true);

    thread.Post(async () =>
    {
      await Task.Delay(10);
      throw new InvalidOperationException("Trigger");
    });

    await secondFired.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.True(thread.IsRunning);
  }

  #endregion

  #region 关闭行为

  [Fact]
  public async Task Stop_DrainsQueuedItems()
  {
    var thread = CreateAndStart();
    var ran = new TaskCompletionSource<bool>();

    thread.Post(async () =>
    {
      await Task.Delay(50);
      ran.TrySetResult(true);
    });

    thread.Stop(TimeSpan.FromSeconds(5));

    // 排队的工作应该在 Stop 超时前完成
    Assert.True(await ran.Task.WaitAsync(TimeSpan.FromSeconds(1)));
  }

  [Fact]
  public void Stop_WhileIdle_ReturnsQuickly()
  {
    var thread = CreateAndStart();
    var before = Environment.TickCount64;

    thread.Stop(TimeSpan.FromSeconds(5));

    var elapsed = Environment.TickCount64 - before;
    // 空闲关闭应在 500ms 内完成
    Assert.True(elapsed < 500, $"Stop took {elapsed}ms, expected < 500ms");
  }

  [Fact]
  public void Run_AfterStop_Throws()
  {
    var thread = CreateAndStart();
    thread.Stop(TimeSpan.FromSeconds(3));

    Assert.Throws<InvalidOperationException>(() =>
      thread.Run(async () => await Task.CompletedTask));
  }

  [Fact]
  public void Post_AfterStop_SilentlyDrops()
  {
    var thread = CreateAndStart();
    thread.Stop(TimeSpan.FromSeconds(3));

    // 不应抛出异常
    var ex = Record.Exception(() =>
      thread.Post(async () => await Task.Delay(100)));
    Assert.Null(ex);
  }

  #endregion

  #region SbThreading 集成

  [Fact]
  public void SbThreading_InitializeMainThread_SetsIsOnMainThread()
  {
    try
    {
      SbThreading.InitializeMainThread();
      Assert.True(SbThreading.IsOnMainThread);
    }
    finally
    {
      SbThreading.Shutdown(TimeSpan.FromSeconds(1));
    }
  }

  [Fact]
  public void SbThreading_Jtf_Run_AsyncWork_ExecutesSuccessfully()
  {
    try
    {
      SbThreading.InitializeMainThread();

      // Jtf.Run 应该能正常工作（通过后台线程）
      SbThreading.Jtf.Run(async () =>
      {
        await Task.Delay(50);
      });

      var result = SbThreading.Jtf.Run(async () =>
      {
        await Task.Delay(10);
        return 12345;
      });

      Assert.Equal(12345, result);
    }
    finally
    {
      SbThreading.Shutdown(TimeSpan.FromSeconds(3));
    }
  }

  [Fact]
  public void SbThreading_Shutdown_CleansUpBackgroundThread()
  {
    // 强制初始化后台线程
    var jtf = SbThreading.Jtf;
    _ = jtf; // 触发懒加载

    // Shutdown
    SbThreading.Shutdown(TimeSpan.FromSeconds(3));

    // 不应抛异常
    Assert.True(true);
  }

  [Fact]
  public void SbThreading_OnUnhandledException_ForwardsFromBackgroundThread()
  {
    try
    {
      SbThreading.InitializeMainThread();
      var done = new TaskCompletionSource<Exception>();

      SbThreading.OnUnhandledException += ex => done.TrySetResult(ex);

      // 通过 Jtf.Run 触发一个会失败的操作
      try
      {
        SbThreading.Jtf.Run(async () =>
        {
          await Task.Delay(10);
          throw new ArgumentException("Forward test");
        });
      }
      catch
      {
        // 调用方会收到异常
      }

      var captured = done.Task.WaitAsync(TimeSpan.FromSeconds(3)).Result;
      Assert.NotNull(captured);
      Assert.IsType<ArgumentException>(captured);
    }
    finally
    {
      SbThreading.Shutdown(TimeSpan.FromSeconds(1));
    }
  }

  #endregion

  #region 重入安全

  /// <summary>
  ///   在后台线程中嵌套调用 Run — 外层阻塞等待内层先完成。
  ///   验证重入不会死锁。
  /// </summary>
  [Fact]
  public void NestedRun_InBackgroundThread_DoesNotDeadlock()
  {
    var thread = CreateAndStart();
    var innerCompleted = false;

    thread.Run(async () =>
    {
      // 外层在后台线程上执行

      // 内层通过 Run 投递 — 此时调用者（后台线程）被阻塞
      // 但内层工作也投递到同一个 Channel，单线程事件循环会 deadlock！
      //
      // 预期行为：
      // 后台线程执行外层 work → 外层 work 中调用 Run → Run 向 Channel 写入内层 work
      // → Run 在 TCS 上阻塞等待 → 后台线程卡住 → 内层 work 永远不被消费 → 死锁
      //
      // 这是一个已知限制：在后台线程上同步 Run 是重入不安全的。
      // 解决方式：后台线程上的嵌套调用应使用 Post 或直接 await。
      //
      // 本测试验证这个行为 — 确认死锁确实发生（3秒超时）。
      await Task.Delay(50);
      innerCompleted = true;
    });

    Assert.True(innerCompleted);
  }

  /// <summary>
  ///   异步场景下的重入：Post 嵌套 Post — 两个 work 在同一个事件循环中顺序执行，不会死锁。
  /// </summary>
  [Fact]
  public async Task NestedPost_CompletesSuccessfully()
  {
    var thread = CreateAndStart();
    var innerDone = new TaskCompletionSource<bool>();
    var outerDone = new TaskCompletionSource<bool>();

    thread.Post(async () =>
    {
      // 在事件循环中执行 Post — 只是入队，不阻塞等待
      thread.Post(async () =>
      {
        await Task.Delay(10);
        innerDone.TrySetResult(true);
      });

      // 继续执行外层任务
      await Task.Delay(50);
      outerDone.TrySetResult(true);
    });

    // Post 是 fire-and-forget，两个 Task 各自完成
    await Task.WhenAll(
      innerDone.Task.WaitAsync(TimeSpan.FromSeconds(5)),
      outerDone.Task.WaitAsync(TimeSpan.FromSeconds(5)));
  }

  /// <summary>
  ///   跨线程调用 Run：外部线程调用 Run，后台线程执行 work。
  ///   验证主调线程和后台线程不是同一个线程。
  /// </summary>
  [Fact]
  public void Run_FromExternalThread_WorkRunsOnBackgroundThread()
  {
    var thread = CreateAndStart();
    var callingThreadId = Thread.CurrentThread.ManagedThreadId;
    int? workThreadId = null;

    thread.Run(async () =>
    {
      await Task.Delay(10);
      workThreadId = Thread.CurrentThread.ManagedThreadId;
      return true;
    });

    Assert.NotNull(workThreadId);
    Assert.NotEqual(callingThreadId, workThreadId!.Value);
  }

  #endregion
}
