using System;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System;
using Sb.Extensions.System.Threading;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   Modbus 线圈
/// </summary>
public class ModbusCoils
{
  /// <summary>
  ///   基本数据
  /// </summary>
  private readonly byte[] _data = new byte[ushort.MaxValue / 8];

  /// <summary>
  ///   锁
  /// </summary>
  private readonly AsyncLock _lock = new();

  /// <summary>
  ///   上锁
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public LockedModbusCoils Lock(CancellationToken ct = default)
  {
    var l = _lock.Lock(ct);
    return new LockedModbusCoils(_data, in l);
  }

  /// <summary>
  ///   上锁
  /// </summary>
  /// <param name="ct"></param>
  /// <returns></returns>
  public async ValueTask<LockedModbusCoils> LockAsync(CancellationToken ct = default)
  {
    var l = await _lock.LockAsync(ct);
    return new LockedModbusCoils(_data, in l);
  }

  /// <summary>
  ///   上锁的
  /// </summary>
  public readonly struct LockedModbusCoils : IDisposable, IAsyncDisposable
  {
    private readonly AsyncLock.InnerLock _innerLock;
    private readonly byte[] _data;

    /// <summary>
    /// </summary>
    /// <param name="data"></param>
    /// <param name="innerLock"></param>
    internal LockedModbusCoils(byte[] data, scoped in AsyncLock.InnerLock innerLock)
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
    public BitSpan Data => new(_data);

    /// <summary>
    ///   获取数据
    /// </summary>
    /// <param name="index"></param>
    public bool this[int index]
    {
      get => Data.Get(index);
      set => Data.Set(index, value);
    }

    /// <summary>
    ///   翻转
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public bool Toggle(int index)
    {
      var result = !Data.Get(index);
      Data.Set(index, result);

      return result;
    }
  }
}

/// <summary>
///   Modbus 写事件参数
/// </summary>
public readonly struct ModbusCoilsHandlerArgs
{
  /// <summary>
  /// </summary>
  /// <param name="data"></param>
  /// <param name="offset"></param>
  /// <param name="count"></param>
  public ModbusCoilsHandlerArgs(ModbusCoils.LockedModbusCoils data, int offset, int count)
  {
    Data = data;
    _oldData = new byte[(count + 7) >> 3];
    data.Data.CopyTo(_oldData.AsSpan(), offset, count);
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
  public ModbusCoils.LockedModbusCoils Data { get; }
}

/// <summary>
///   ModbusCoils 写事件
/// </summary>
/// <param name="start">注册的起始</param>
/// <param name="count">注册的长度</param>
/// <param name="action"></param>
public class ModbusCoilsWriteHandler(int start, int count, Action<ModbusCoilsHandlerArgs> action)
{
  /// <summary>注册的起始</summary>
  public int Start { get; init; } = start;

  /// <summary>注册的长度</summary>
  public int Count { get; init; } = count;

  /// <summary></summary>
  public Action<ModbusCoilsHandlerArgs> Action { get; init; } = action;
}
