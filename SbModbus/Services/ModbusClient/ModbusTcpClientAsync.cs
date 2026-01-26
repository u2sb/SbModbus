using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Models;

namespace SbModbus.Services.ModbusClient;

public partial class ModbusTcpClient
{
  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).WithEndianness(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);

    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    var result = await WriteAndReadWithTimeoutAsync(temp, length, ReadTimeout, ct);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress,
    int count, CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress,
      ConvertUshort(count).WithEndianness(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    var result = await WriteAndReadWithTimeoutAsync(temp, length, ReadTimeout, ct);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value,
    CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      value ? [0xFF, 0x00] : "\0\0"u8);

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    _ = await WriteAndReadWithTimeoutAsync(temp, length, ReadTimeout, ct);
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data,
    CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress, data.Span);

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    _ = await WriteAndReadWithTimeoutAsync(temp, length, ReadTimeout, ct);
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
        writer.Write(ConvertUshort(l / 2).WithEndianness(true));

        // 写字节数
        writer.Write(ConvertByte(l));
      });


    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);

    await WriteAndReadWithTimeoutAsync(temp, length, ReadTimeout, ct);
  }

  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> ReadRegistersAsync(int unitIdentifier,
    ModbusFunctionCode functionCode,
    int startingAddress, int count, CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ConvertUshort(count).WithEndianness(true));

    // 7MBAP 1功能码 1数据长度 2n数据
    var length = 7 + 1 + 1 + count * 2;
    var temp = MemoryMarshal.AsMemory(buffer.WrittenMemory);
    ((ushort)(temp.Length - 6)).WriteTo(temp[4..6].Span, BigAndSmallEndianEncodingMode.ABCD);
    var result = await WriteAndReadWithTimeoutAsync(temp, length, ReadTimeout, ct);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data, int length,
    int readTimeout, CancellationToken ct = default)
  {
    if (!ModbusStream.IsConnected) throw new SbModbusException("Not Connected");

    var tid = data[..2].Span.ToUInt16();

    using var mt = await ModbusStream.LockAsync(ct);
    // 清除读缓存
    await mt.ClearReadBufferAsync(ct);

    // 写入数据 
    await mt.WriteAsync(data, ct);

    OnWrite?.Invoke(data, this);

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
        var read = await mt.ReadAsync(memory[bytesRead..], cts.Token);

        // 如果没读到数据就跳过
        if (read == 0) continue;

        bytesRead += read;

        // 如果已经读到功能码和错误码
        if (!verificationFunctionCode && bytesRead >= 9)
        {
          // 如果功能码不一致
          if ((buffer[7] & 0x80) != 0) length = 9; // 相应不正常时返回值长度变为9
          verificationFunctionCode = true;
        }

        await Task.Yield();
      }

      var result = memory[..bytesRead];

      VerifyFrame(result.Span, tid);

      OnRead?.Invoke(result, this);

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
}