using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SbBitConverter.Attributes;
using SbBitConverter.Utils;
using SbModbus.Models;
using BitConverter = SbBitConverter.Utils.BitConverter;

namespace SbModbus.Services.ModbusClient;

public partial class ModbusTcpClient
{
  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).ToByteArray(true));

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
      ConvertUshort(count).ToByteArray(true));

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
      ConvertUshort((ushort)(value ? 0x00FF : 0x0000)).ToByteArray(true));

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
        writer.Write(ConvertUshort(l / 2).ToByteArray(true));

        // 写字节数
        writer.Write(ConvertByte(l).ToByteArray());
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
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ConvertUshort(count).ToByteArray(true));

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
    if (!Stream.IsConnected) throw new SbModbusException("Not Connected");

    var tid = BitConverter.ToUInt16(data[..2].Span);

    // 清除读缓存
    await Stream.ClearReadBufferAsync(ct);

    // 写入数据 
    await Stream.WriteAsync(data, ct);

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

      while (!cts.Token.IsCancellationRequested && bytesRead < length)
      {
        var read = await Stream.ReadAsync(memory[bytesRead..], cts.Token);

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

      if (cts.Token.IsCancellationRequested) throw new OperationCanceledException();

      var result = memory[..bytesRead];

      VerifyFrame(result.Span, tid);

      return result;
    }
    catch (OperationCanceledException)
    {
      throw new SbModbusException($"Timeout occurred after {readTimeout} milliseconds");
    }
  }
}