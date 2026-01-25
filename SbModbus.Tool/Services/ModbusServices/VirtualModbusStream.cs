using System.IO;
using SbModbus.Models;

namespace SbModbus.Tool.Services.ModbusServices;

/// <summary>
///   虚拟Modbus接口
/// </summary>
public class VirtualModbusStream : ModbusStream
{
  public override Stream? BaseStream { get; protected set; }
  public override bool IsConnected { get; } = true;
  public override int ReadTimeout { get; set; }
  public override int WriteTimeout { get; set; }

  public override void Dispose()
  {
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

  protected override void Write(ReadOnlySpan<byte> buffer)
  {
  }

  protected override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
  {
    Write(buffer.Span);
    return ValueTask.CompletedTask;
  }

  /// <inheritdoc />
  protected override int Read(Span<byte> buffer)
  {
    return 0;
  }

  /// <inheritdoc />
  protected override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
  {
    return ValueTask.FromResult(Read(buffer.Span));
  }
}