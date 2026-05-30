using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System;
using Sb.Extensions.System.Threading;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   Modbus 寄存器
/// </summary>
public class ModbusRegisters
{
  private readonly byte[] _data = new byte[ushort.MaxValue * 2];

  /// <summary>
  ///   锁
  /// </summary>
  private readonly AsyncLock _lock = new();

  /// <summary>
  ///   上锁
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public LockedModbusRegisters Lock(CancellationToken ct = default)
  {
    var l = _lock.Lock(ct);
    return new LockedModbusRegisters(_data, in l);
  }

  /// <summary>
  ///   上锁
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public async ValueTask<LockedModbusRegisters> LockAsync(CancellationToken ct = default)
  {
    var l = await _lock.LockAsync(ct);
    return new LockedModbusRegisters(_data, in l);
  }

  /// <summary>
  ///   上锁的
  /// </summary>
  public readonly struct LockedModbusRegisters : IDisposable, IAsyncDisposable
  {
    private readonly AsyncLock.InnerLock _innerLock;
    private readonly byte[] _data;

    /// <summary>
    /// </summary>
    /// <param name="data"></param>
    /// <param name="innerLock"></param>
    internal LockedModbusRegisters(byte[] data, scoped in AsyncLock.InnerLock innerLock)
    {
      _data = data;
      _innerLock = innerLock;
    }

    /// <inheritdoc />
    public void Dispose()
    {
      _innerLock.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      await _innerLock.DisposeAsync();
    }

    /// <summary>
    ///   数据
    /// </summary>
    public Span<byte> Data => _data.AsSpan();

    /// <summary>
    ///   获取数据
    /// </summary>
    /// <param name="index"></param>
    public ushort this[int index]
    {
      get => Data[(index * 2)..].ToUInt16();
      set => MemoryMarshal.Write(Data[(index * 2)..],
#if NET8_0_OR_GREATER
        in
#else
        ref
#endif
        value);
    }
  }
}

/// <summary>
///   Modbus 写事件参数
/// </summary>
public readonly struct ModbusRegistersHandlerArgs
{
  /// <summary>
  /// </summary>
  /// <param name="data"></param>
  /// <param name="offset"></param>
  /// <param name="count"></param>
  public ModbusRegistersHandlerArgs(ModbusRegisters.LockedModbusRegisters data, int offset, int count)
  {
    Data = data;
    _oldData = data.Data.Slice(offset, count).ToArray();
  }

  private readonly byte[] _oldData;

  /// <summary>
  ///   旧数据
  ///   注意旧数据是做过偏移的 0 就是 offset
  /// </summary>
  public BitSpan OldSpan => new(_oldData);

  /// <summary>
  ///   数据
  /// </summary>
  public ModbusRegisters.LockedModbusRegisters Data { get; }
}

/// <summary>
///   ModbusRegisters 写事件
/// </summary>
/// <param name="start">注册的寄存器起始</param>
/// <param name="count">注册的寄存器长度</param>
/// <param name="action"></param>
public class ModbusRegistersWriteHandler(int start, int count, Action<ModbusRegistersHandlerArgs> action)
{
  /// <summary>注册的起始</summary>
  public int Start { get; init; } = start;

  /// <summary>注册的长度</summary>
  public int Count { get; init; } = count;

  /// <summary></summary>
  public Action<ModbusRegistersHandlerArgs> Action { get; init; } = action;
}
