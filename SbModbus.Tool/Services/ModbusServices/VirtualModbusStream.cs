using SbModbus.Models;

namespace SbModbus.Tool.Services.ModbusServices;

/// <summary>
///   虚拟Modbus接口
/// </summary>
public class VirtualModbusStream : ModbusStream
{
  public override bool IsConnected { get; } = true;
  public override int ReadTimeout { get; set; }
  public override int WriteTimeout { get; set; }

  public override void Dispose()
  {
    base.Dispose();
    StreamLock.Dispose();
  }

  public override bool Connect()
  {
    return true;
  }

  public override bool Disconnect()
  {
    return true;
  }

  protected override ValueTask ClearReadBufferAsync(CancellationToken ct = new())
  {
    return ValueTask.CompletedTask;
  }

  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  protected override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    return ValueTask.FromResult(0);
  }
}
