using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance.Buffers;
using Sb.Extensions.System;
using SbModbus.Models;

namespace SbModbus.Services.ModbusServer;

/// <inheritdoc />
public abstract class BaseModbusServer : IModbusServer
{
  private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

  private readonly Channel<ModbusFrameMessage> _frameChannel =
    Channel.CreateUnbounded<ModbusFrameMessage>(
      new UnboundedChannelOptions
      {
        SingleReader = true,
        SingleWriter = true
      });

  private CancellationTokenSource? _runningCts;
  private IModbusStreamServer? _server;

  /// <inheritdoc />
  public IReadOnlyList<IModbusStream> Sessions => _sessions.Values.Select(s => s.Stream).ToList().AsReadOnly();

  /// <inheritdoc />
  public bool IsRunning => _runningCts is { IsCancellationRequested: false };

  /// <inheritdoc />
  public ModbusCoils Coils { get; } = new();

  /// <inheritdoc />
  public ModbusCoils DiscreteInputs { get; } = new();

  /// <inheritdoc />
  public ModbusRegisters HoldingRegisters { get; } = new();

  /// <inheritdoc />
  public ModbusRegisters InputRegisters { get; } = new();

  /// <inheritdoc />
  public SortedSet<byte> UnitIdentifier { get; } = [1];

  /// <inheritdoc />
  public event Action<IModbusStream>? OnSessionConnected;

  /// <inheritdoc />
  public event Action<IModbusStream>? OnSessionDisconnected;

  #region 生命周期

  /// <inheritdoc />
  public async Task StartAsync(IModbusStreamServer server, CancellationToken ct = default)
  {
    if (IsRunning) return;

    _server = server;
    _runningCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

    // 注入帧解析器（由子类决定 RTU / TCP 解析策略）
    server.FrameParser = FrameParser;

    // 订阅会话事件（用于跟踪 Sessions 列表）
    server.OnSessionConnected += OnServerSessionConnected;
    server.OnSessionDisconnected += OnServerSessionDisconnected;

    // 注册所有站号到 StreamServer，让 StreamServer 按 StationId 路由帧
    foreach (var unitId in UnitIdentifier)
      server.RegisterStation(unitId, _frameChannel.Writer);

    if (!server.IsListening) server.Start();

    try
    {
      // 从自己的 Channel 读取解析后的帧并处理
      await ProcessLoopAsync(_runningCts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // 正常的停止流程
    }
    finally
    {
      // 取消注册
      foreach (var unitId in UnitIdentifier)
        server.UnregisterStation(unitId);
      server.OnSessionConnected -= OnServerSessionConnected;
      server.OnSessionDisconnected -= OnServerSessionDisconnected;
      _server = null;
    }
  }

  /// <inheritdoc />
  public Task StopAsync(CancellationToken ct = default)
  {
    if (IsRunning)
    {
      var server = _server;
      if (server != null)
        foreach (var unitId in UnitIdentifier)
          server.UnregisterStation(unitId);
      CancelAndDispose(ref _runningCts);
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public virtual void Dispose()
  {
    try
    {
      StopAsync().GetAwaiter().GetResult();
    }
    catch
    {
    }

    CancelAndDispose(ref _runningCts);

    _frameChannel.Writer.TryComplete();

    _sessions.Clear();
    _server = null;

    OnSessionConnected = null;
    OnSessionDisconnected = null;

    GC.SuppressFinalize(this);
  }

  #endregion

  #region 会话管理

  private void OnServerSessionConnected(IModbusStream stream)
  {
    _sessions.TryAdd(Guid.NewGuid().ToString(), new SessionState(stream));
    Logger.Log(LogLevel.Information, $"Session connected ({stream.GetTransportInfo()})");

    try
    {
      OnSessionConnected?.Invoke(stream);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnSessionConnected callback threw an exception");
    }
  }

  private void OnServerSessionDisconnected(IModbusStream stream)
  {
    foreach (var kv in _sessions)
    {
      if (ReferenceEquals(kv.Value.Stream, stream))
      {
        _sessions.TryRemove(kv.Key, out _);
        break;
      }
    }

    Logger.Log(LogLevel.Information, $"Session disconnected ({stream.GetTransportInfo()})");

    try
    {
      OnSessionDisconnected?.Invoke(stream);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, "OnSessionDisconnected callback threw an exception");
    }
  }

  #endregion

  #region Channel 处理循环

  /// <summary>
  ///   从自己的 Channel 读取解析后的请求帧，处理并发送响应
  /// </summary>
  private async Task ProcessLoopAsync(CancellationToken ct)
  {
    await foreach (var message in _frameChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
    {
      try
      {
        using var response = await ProcessRequestAsync(
          message.UnitId, message.FunctionCode,
          message.StartingAddress, message.Quantity, message.Data, ct
        ).ConfigureAwait(false);

        if (response.SuppressResponse)
          continue;

        using var mt = await message.Session.LockAsync(ct).ConfigureAwait(false);

        if (response.IsError)
          await SendErrorResponseFrameAsync(mt, message.Tid, message.UnitId,
            message.FunctionCode, response.ExceptionCode!.Value, ct).ConfigureAwait(false);
        else
          await SendResponseFrameAsync(mt, message.Tid, message.UnitId,
            message.FunctionCode, response.Data!.Value, ct).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        break;
      }
      catch (SbModbusException ex)
      {
        Logger.Log(LogLevel.Warning, $"Processing error: {ex.Message}");
      }
      catch (Exception ex)
      {
        Logger.Error(ex, "Unexpected processing error");
      }
    }
  }

  #endregion

  /// <summary>
  ///   检查地址范围是否超出可访问区域
  /// </summary>
  private static bool IsAddressOutOfRange(ushort addr, ushort qty)
  {
    if (qty == 0) return true;
    return addr + qty > ushort.MaxValue + 1;
  }

  #region 抽象响应方法

  /// <summary>
  ///   帧解析委托 — 子类返回对应的解析策略。
  ///   <see cref="SbModbus.Services.ModbusServer.ModbusRtuServer" /> 返回 RTU 解析；
  ///   <see cref="SbModbus.Services.ModbusServer.ModbusTcpServer" /> 返回 TCP 解析。
  /// </summary>
  protected abstract TryParseFrameDelegate FrameParser { get; }

  /// <summary>
  ///   构建并发送正常响应帧（子类实现 RTU/TCP 格式差异）
  /// </summary>
  protected abstract ValueTask SendResponseFrameAsync(
    ModbusStream.LockedModbusStream stream, ushort tid, byte unitId,
    ModbusFunctionCode functionCode, ReadOnlyMemory<byte> pduBody, CancellationToken ct);

  /// <summary>
  ///   构建并发送错误响应帧
  /// </summary>
  protected abstract ValueTask SendErrorResponseFrameAsync(
    ModbusStream.LockedModbusStream stream, ushort tid, byte unitId,
    ModbusFunctionCode functionCode, ModbusExceptionCode exceptionCode, CancellationToken ct);

  #endregion

  #region 请求分发

  /// <summary>
  ///   处理 Modbus 请求 — 按功能码分发到具体的读写操作
  /// </summary>
  /// <returns>响应结果（PDU 体或异常码）</returns>
  protected async ValueTask<ModbusResponseResult> ProcessRequestAsync(
    byte unitId, ModbusFunctionCode functionCode,
    ushort startingAddress, ushort quantity, ReadOnlyMemory<byte> requestData,
    CancellationToken ct)
  {
    // 检查站号 — 不匹配则静默忽略
    if (!UnitIdentifier.Contains(unitId))
      return ModbusResponseResult.SilentlyIgnore();

    // 地址边界检查
    if (IsAddressOutOfRange(startingAddress, quantity))
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataAddress);

    try
    {
      switch (functionCode)
      {
        case ModbusFunctionCode.ReadCoils:
          return await ReadBitsAsync(Coils, startingAddress, quantity, ct).ConfigureAwait(false);

        case ModbusFunctionCode.ReadDiscreteInputs:
          return await ReadBitsAsync(DiscreteInputs, startingAddress, quantity, ct).ConfigureAwait(false);

        case ModbusFunctionCode.ReadHoldingRegisters:
          return await ReadRegistersInternalAsync(HoldingRegisters, startingAddress, quantity, ct)
            .ConfigureAwait(false);

        case ModbusFunctionCode.ReadInputRegisters:
          return await ReadRegistersInternalAsync(InputRegisters, startingAddress, quantity, ct)
            .ConfigureAwait(false);

        case ModbusFunctionCode.WriteSingleCoil:
          return await WriteSingleCoilInternalAsync(startingAddress, requestData, ct).ConfigureAwait(false);

        case ModbusFunctionCode.WriteSingleRegister:
          return await WriteSingleRegisterInternalAsync(startingAddress, requestData, ct).ConfigureAwait(false);

        case ModbusFunctionCode.WriteMultipleCoils:
          return await WriteMultipleCoilsInternalAsync(startingAddress, quantity, requestData, ct)
            .ConfigureAwait(false);

        case ModbusFunctionCode.WriteMultipleRegisters:
          return await WriteMultipleRegistersInternalAsync(startingAddress, quantity, requestData, ct)
            .ConfigureAwait(false);

        default:
          return ModbusResponseResult.Error(ModbusExceptionCode.IllegalFunction);
      }
    }
    catch (SbModbusException ex) when (ex.ExceptionCode != SbModbusException.NoSpecificCode)
    {
      Logger.Log(LogLevel.Warning,
        $"Modbus exception: FC=0x{(int)functionCode:X2}, code=0x{(int)ex.ExceptionCode:X2}, {ex.Message}");
      return ModbusResponseResult.Error(ex.ExceptionCode);
    }
    catch (Exception ex)
    {
      Logger.Error(ex, $"Unexpected error processing request FC=0x{(int)functionCode:X2}");
      return ModbusResponseResult.Error(ModbusExceptionCode.ServerDeviceFailure);
    }
  }

  #endregion

  #region 读操作

  /// <summary>
  ///   读取位数据（线圈 / 离散输入）
  /// </summary>
  private static async ValueTask<ModbusResponseResult> ReadBitsAsync(
    ModbusCoils coils, int startingAddress, int quantity, CancellationToken ct)
  {
    var byteCount = quantity + 7 >> 3;
    var owner = MemoryOwner<byte>.Allocate(1 + byteCount);
    owner.Span[0] = (byte)byteCount;

    await using(var locked = await coils.LockAsync(ct).ConfigureAwait(false))
    {
      locked.Data.CopyTo(owner.Span.Slice(1), startingAddress, quantity);
    }

    return ModbusResponseResult.Success(owner);
  }

  /// <summary>
  ///   读取寄存器数据（保持寄存器 / 输入寄存器）
  /// </summary>
  private static async ValueTask<ModbusResponseResult> ReadRegistersInternalAsync(
    ModbusRegisters registers, int startingAddress, int quantity, CancellationToken ct)
  {
    var byteCount = quantity * 2;
    var owner = MemoryOwner<byte>.Allocate(1 + byteCount);
    owner.Span[0] = (byte)byteCount;

    await using(var locked = await registers.LockAsync(ct).ConfigureAwait(false))
    {
      locked.Data.Slice(startingAddress * 2, quantity * 2).CopyTo(owner.Span.Slice(1));
    }

    return ModbusResponseResult.Success(owner);
  }

  #endregion

  #region 写操作 — 线圈

  /// <summary>
  ///   FC05 — 写单个线圈
  /// </summary>
  private async ValueTask<ModbusResponseResult> WriteSingleCoilInternalAsync(
    ushort startingAddress, ReadOnlyMemory<byte> requestData, CancellationToken ct)
  {
    if (requestData.Length != 2)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    var s = requestData.Span;
    var isOn = s[0] == 0xFF && s[1] == 0x00;
    var isOff = s[0] == 0x00 && s[1] == 0x00;
    if (!isOn && !isOff)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    // 在 await 之前提取值（Span 不能跨 await）
    var d0 = s[0];
    var d1 = s[1];

    await using var locked = await Coils.LockAsync(ct).ConfigureAwait(false);
    var args = new ModbusCoilsHandlerArgs(locked, startingAddress, 1);
    locked.Data.Set(startingAddress, isOn);
    FireCoilsWriteHandlers(in args, startingAddress, 1);

    // 回显: startingAddress(2, BE) + data(2)
    Span<byte> echo = stackalloc byte[4];
    startingAddress.WriteTo(echo, BigAndSmallEndianEncodingMode.ABCD);
    echo[2] = d0;
    echo[3] = d1;

    return ModbusResponseResult.Success(echo.ToArray());
  }

  /// <summary>
  ///   FC15 — 写多个线圈
  /// </summary>
  private async ValueTask<ModbusResponseResult> WriteMultipleCoilsInternalAsync(
    ushort startingAddress, ushort quantity, ReadOnlyMemory<byte> requestData, CancellationToken ct)
  {
    if (requestData.Length < 1)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    // 在 await 之前提取数据（ReadOnlyMemory 可跨 await）
    var byteCount = requestData.Span[0];
    var expectedByteCount = quantity + 7 >> 3;
    if (byteCount != expectedByteCount)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    var coilBytesMemory = requestData.Slice(1, byteCount);

    await using var locked = await Coils.LockAsync(ct).ConfigureAwait(false);
    var args = new ModbusCoilsHandlerArgs(locked, startingAddress, quantity);
    var coilBytes = coilBytesMemory.Span;

    // 逐位写入
    for (var i = 0; i < quantity; i++)
    {
      var byteIndex = i >> 3;
      var bitIndex = i & 7;
      var value = (coilBytes[byteIndex] & 1 << bitIndex) != 0;
      locked.Data.Set(startingAddress + i, value);
    }

    FireCoilsWriteHandlers(in args, startingAddress, quantity);

    // 回显: startingAddress(2, BE) + quantity(2, BE)
    Span<byte> echo = stackalloc byte[4];
    startingAddress.WriteTo(echo, BigAndSmallEndianEncodingMode.ABCD);
    quantity.WriteTo(echo.Slice(2), BigAndSmallEndianEncodingMode.ABCD);

    return ModbusResponseResult.Success(echo.ToArray());
  }

  /// <summary>
  ///   触发匹配的线圈写事件处理器
  /// </summary>
  private void FireCoilsWriteHandlers(in ModbusCoilsHandlerArgs args, int writeStart, int writeCount)
  {
    var writeEnd = writeStart + writeCount;

    lock (CoilsWriteHandlersLocker)
    {
      foreach (var handler in CoilsWriteHandlers
                 .Select(handler => new
                 {
                   handler,
                   handlerEnd = handler.Start + handler.Count
                 })
                 .Where(t => t.handler.Start < writeEnd && t.handlerEnd > writeStart)
                 .Select(t => t.handler))
      {
        try
        {
          handler.Action(args);
        }
        catch (Exception ex)
        {
          Logger.Error(ex, $"Coils write handler [{handler.Start}, {handler.Count}] threw exception");
        }
      }
    }
  }

  #endregion

  #region 写操作 — 寄存器

  /// <summary>
  ///   FC06 — 写单个寄存器
  /// </summary>
  private async ValueTask<ModbusResponseResult> WriteSingleRegisterInternalAsync(
    ushort startingAddress, ReadOnlyMemory<byte> requestData, CancellationToken ct)
  {
    if (requestData.Length != 2)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    await using var locked = await HoldingRegisters.LockAsync(ct).ConfigureAwait(false);
    var args = new ModbusRegistersHandlerArgs(locked, startingAddress, 1);
    requestData.Span.CopyTo(locked.Data.Slice(startingAddress * 2, 2));
    FireRegistersWriteHandlers(in args, startingAddress, 1);

    // 回显: startingAddress(2, BE) + data(2)
    Span<byte> echo = stackalloc byte[4];
    startingAddress.WriteTo(echo, BigAndSmallEndianEncodingMode.ABCD);
    requestData.Span.CopyTo(echo.Slice(2));

    return ModbusResponseResult.Success(echo.ToArray());
  }

  /// <summary>
  ///   FC16 — 写多个寄存器
  /// </summary>
  private async ValueTask<ModbusResponseResult> WriteMultipleRegistersInternalAsync(
    ushort startingAddress, ushort quantity, ReadOnlyMemory<byte> requestData, CancellationToken ct)
  {
    if (requestData.Length < 1)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    // 在 await 之前提取（ReadOnlyMemory 可跨 await）
    var byteCount = requestData.Span[0];
    var expectedByteCount = quantity * 2;
    if (byteCount != expectedByteCount)
      return ModbusResponseResult.Error(ModbusExceptionCode.IllegalDataValue);

    var registerBytesMemory = requestData.Slice(1, byteCount);

    await using var locked = await HoldingRegisters.LockAsync(ct).ConfigureAwait(false);
    var args = new ModbusRegistersHandlerArgs(locked, startingAddress, quantity);
    registerBytesMemory.Span.CopyTo(locked.Data.Slice(startingAddress * 2, quantity * 2));

    FireRegistersWriteHandlers(in args, startingAddress, quantity);

    // 回显: startingAddress(2, BE) + quantity(2, BE)
    Span<byte> echo = stackalloc byte[4];
    startingAddress.WriteTo(echo, BigAndSmallEndianEncodingMode.ABCD);
    quantity.WriteTo(echo.Slice(2), BigAndSmallEndianEncodingMode.ABCD);

    return ModbusResponseResult.Success(echo.ToArray());
  }

  /// <summary>
  ///   触发匹配的寄存器写事件处理器
  /// </summary>
  private void FireRegistersWriteHandlers(in ModbusRegistersHandlerArgs args, int writeStart, int writeCount)
  {
    var writeEnd = writeStart + writeCount;

    lock (RegistersWriteHandlersLocker)
    {
      foreach (var handler in RegistersWriteHandlers
                 .Select(handler => new
                 {
                   handler,
                   handlerEnd = handler.Start + handler.Count
                 })
                 .Where(t => t.handler.Start < writeEnd && t.handlerEnd > writeStart)
                 .Select(t => t.handler))
      {
        try
        {
          handler.Action(args);
        }
        catch (Exception ex)
        {
          Logger.Error(ex, $"Registers write handler [{handler.Start}, {handler.Count}] threw exception");
        }
      }
    }
  }

  #endregion

  #region 写事件（public API）

  /// <summary>
  ///   线圈写事件锁
  /// </summary>
  protected readonly
#if NET10_0_OR_GREATER
    Lock
#else
    object
#endif
    CoilsWriteHandlersLocker = new();

  /// <summary>
  ///   寄存器写事件锁
  /// </summary>
  protected readonly
#if NET10_0_OR_GREATER
    Lock
#else
    object
#endif
    RegistersWriteHandlersLocker = new();

  /// <summary>
  ///   线圈写事件
  /// </summary>
  protected List<ModbusCoilsWriteHandler> CoilsWriteHandlers { get; } = [];

  /// <summary>
  ///   寄存器写事件
  /// </summary>
  protected List<ModbusRegistersWriteHandler> RegistersWriteHandlers { get; } = [];

  /// <inheritdoc />
  public void AddHandler(ModbusCoilsWriteHandler handler)
  {
    lock (CoilsWriteHandlersLocker)
    {
      if (CoilsWriteHandlers.Contains(handler)) return;

      CoilsWriteHandlers.Add(handler);
      CoilsWriteHandlers.Sort((p1, p2) => p1.Start.CompareTo(p2.Start));
    }
  }

  /// <inheritdoc />
  public void RemoveHandler(ModbusCoilsWriteHandler handler)
  {
    lock (CoilsWriteHandlersLocker)
    {
      CoilsWriteHandlers.Remove(handler);
    }
  }

  /// <inheritdoc />
  public void AddHandler(ModbusRegistersWriteHandler handler)
  {
    lock (RegistersWriteHandlersLocker)
    {
      if (RegistersWriteHandlers.Contains(handler)) return;

      RegistersWriteHandlers.Add(handler);
      RegistersWriteHandlers.Sort((p1, p2) => p1.Start.CompareTo(p2.Start));
    }
  }

  /// <inheritdoc />
  public void RemoveHandler(ModbusRegistersWriteHandler handler)
  {
    lock (RegistersWriteHandlersLocker)
    {
      RegistersWriteHandlers.Remove(handler);
    }
  }

  #endregion

  #region 内部类型

  /// <summary>
  ///   请求处理结果。当 <see cref="Data" /> 由 <see cref="MemoryOwner{T}" /> 支持时，
  ///   消费方必须在读取 <see cref="Data" /> 后调用 <see cref="Dispose" /> 归还内存。
  /// </summary>
  protected struct ModbusResponseResult : IDisposable
  {
    private MemoryOwner<byte>? _owner;

    /// <summary>正常响应的 PDU 体数据</summary>
    public ReadOnlyMemory<byte>? Data { get; private set; }

    /// <summary>异常码（null 表示正常）</summary>
    public ModbusExceptionCode? ExceptionCode { get; private set; }

    /// <summary>是否为错误响应</summary>
    public readonly bool IsError => ExceptionCode.HasValue;

    /// <summary>是否抑制响应（静默忽略请求）</summary>
    public bool SuppressResponse { get; private set; }

    /// <summary>创建成功结果 — 由 <see cref="MemoryOwner{T}" /> 接管内存所有权</summary>
    public static ModbusResponseResult Success(MemoryOwner<byte> owner)
    {
      return new ModbusResponseResult
      {
        _owner = owner,
        Data = owner.Memory
      };
    }

    /// <summary>创建成功结果 — 使用已有的 <see cref="ReadOnlyMemory{T}" />（无所有权）</summary>
    public static ModbusResponseResult Success(ReadOnlyMemory<byte> data)
    {
      return new ModbusResponseResult
      {
        Data = data
      };
    }

    /// <summary>创建错误结果</summary>
    public static ModbusResponseResult Error(ModbusExceptionCode code)
    {
      return new ModbusResponseResult
      {
        ExceptionCode = code
      };
    }

    /// <summary>创建静默忽略结果（不发任何响应）</summary>
    public static ModbusResponseResult SilentlyIgnore()
    {
      return new ModbusResponseResult
      {
        SuppressResponse = true
      };
    }

    /// <summary>
    ///   释放内存所有者（如果有）。
    ///   消费方在读取 <see cref="Data" /> 后应调用此方法。
    /// </summary>
    public void Dispose()
    {
      _owner?.Dispose();
      _owner = null;
    }
  }

  /// <summary>
  ///   会话状态
  /// </summary>
  protected sealed class SessionState
  {
    /// <summary>
    /// </summary>
    /// <param name="stream"></param>
    public SessionState(IModbusStream stream)
    {
      Stream = stream;
    }

    /// <summary>会话流</summary>
    public IModbusStream Stream { get; }
  }

  #endregion

  #region 工具方法

  /// <summary>
  ///   安全取消并释放 CancellationTokenSource
  /// </summary>
  protected static void CancelAndDispose(ref CancellationTokenSource? cts)
  {
    var source = Interlocked.Exchange(ref cts, null);
    if (source == null) return;
    try
    {
      source.Cancel();
    }
    catch (ObjectDisposedException)
    {
    }

    source.Dispose();
  }

  #endregion
}
