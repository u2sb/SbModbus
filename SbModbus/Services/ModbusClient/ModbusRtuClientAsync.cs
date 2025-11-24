using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sb.Extensions.System;
using SbModbus.Models;

namespace SbModbus.Services.ModbusClient;

public partial class ModbusRtuClient
{
  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).ToByteArray(true));

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct);

    // 返回数据
    return result[3..^2];
  }

  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress,
    int count, CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress,
      ConvertUshort(count).ToByteArray(true));

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct);

    // 返回数据
    return result[3..^2];
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value,
    CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress,
      ((ushort)(value ? 0x00FF : 0x0000)).ToByteArray());

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct);
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress, data.Span);

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct);
  }

  /// <inheritdoc />
  public override async ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct = default)
  {
    var l = data.Length;
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress, data.Span,
      writer =>
      {
        // 写寄存器数量
        writer.Write(ConvertUshort(l / 2).ToByteArray(true));

        // 写字节数
        writer.Write(ConvertByte(l).ToByteArray());
      });

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct);
  }

  #region 通用方法

  /// <exception cref="SbModbusException"></exception>
  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data,
    int length,
    int readTimeout, CancellationToken ct = default)
  {
    if (!ModbusStream.IsConnected) throw new SbModbusException("Not Connected");

    // 清除读缓存
    await ModbusStream.ClearReadBufferAsync(ct);

    // 写入数据 
    await ModbusStream.WriteAsync(data, ct);

    OnWrite?.Invoke(data, this);

    // TODO 这里没实现写超时 后续实现

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(readTimeout);
    try
    {
      var buffer = new byte[length];
      var memory = buffer.AsMemory();
      var bytesRead = 0;

      // 是否已验证功能码
      var verificationFunctionCode = false;

      while (bytesRead < length)
      {
        cts.Token.ThrowIfCancellationRequested();
        var read = await ModbusStream.ReadAsync(memory[bytesRead..], cts.Token);

        // 如果没读到数据就跳过
        if (read == 0) continue;

        bytesRead += read;

        // 如果已经读到功能码和错误码
        if (!verificationFunctionCode && bytesRead >= 2)
        {
          // 如果功能码不一致
          if ((buffer[1] & 0x80) != 0) length = 5; // 相应不正常时返回值长度变为5
          verificationFunctionCode = true;
        }

        await Task.Yield();
      }

      var result = memory[..bytesRead];
      
      OnRead?.Invoke(result, this);

      // 验证数据帧
      VerifyFrame(result.Span);

      return result;
    }
    catch (OperationCanceledException)
    {
      throw new SbModbusException($"Timeout occurred after {readTimeout} milliseconds");
    }
    catch (EndOfStreamException)
    {
      throw new SbModbusException($"Timeout occurred after {readTimeout} milliseconds");
    }
  }

  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> ReadRegistersAsync(int unitIdentifier,
    ModbusFunctionCode functionCode, int startingAddress, int count, CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ReadOnlySpan<byte>.Empty, writer =>
    {
      // 写长度
      writer.Write(ConvertUshort(count).ToByteArray(true));
    });


    // 1设备地址 1功能码 1数据长度 2n数据 2校验
    var length = 1 + 1 + 1 + count * 2 + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct);

    // 返回数据
    return result[3..^2];
  }

  #endregion
}