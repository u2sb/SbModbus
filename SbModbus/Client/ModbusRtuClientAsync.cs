using System;
using System.Buffers;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Client;

public partial class ModbusRtuClient
{
  /// <inheritdoc />
  public override async ValueTask<BitArray> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress);
    buffer.Write(ConvertUshort(count).ToBytes(true));
    buffer.WriteCrc16();

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout);

    // 返回数据
    return new BitArray(result[3..^2].ToArray());
  }

  /// <inheritdoc />
  public override async ValueTask<BitArray> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress);
    buffer.Write(ConvertUshort(count).ToBytes(true));
    buffer.WriteCrc16();

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout);

    // 返回数据
    return new BitArray(result[3..^2].ToArray());
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress, bool value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress);

    var data = (ushort)(value ? 0x00FF : 0x0000);

    // 使用小端模式转换数据
    buffer.Write(data.ToBytes());

    buffer.WriteCrc16();

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout);
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress, ushort value)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress);

    // 这里写入时不区分大小端 所以要提前调整好
    buffer.Write(value.ToBytes());

    buffer.WriteCrc16();

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout);
  }


  /// <inheritdoc />
  public override async ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress,
    Memory<byte> data)
  {
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress,
      data.Length + 8);

    // 写寄存器数量
    buffer.Write(ConvertUshort(data.Length / 2).ToBytes(true));

    // 写字节数
    buffer.Write(ConvertByte(data.Length).ToBytes());

    // 写数据
    buffer.Write(data.Span);

    // 写校验
    buffer.WriteCrc16();

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout);
  }

  #region 通用方法

  /// <inheritdoc />
  protected override async ValueTask<Memory<ushort>> ReadRegistersAsync(int unitIdentifier,
    ModbusFunctionCode functionCode,
    int startingAddress, int count)
  {
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress);
    buffer.Write(ConvertUshort(count).ToBytes(true));
    buffer.WriteCrc16();

    // 1设备地址 1功能码 1数据长度 2n数据 2校验
    var length = 1 + 1 + 1 + count * 2 + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout);

    // 返回数据
    return result[3..^2].Cast<byte, ushort>();
  }


  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data, int length,
    int initialTimeout)
  {
    // 写入数据
    await Stream.WriteAsync(data);
    await Stream.FlushAsync();

    if (!Stream.CanRead) throw new ModbusException("Stream can not read");

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(initialTimeout);
    try
    {
      return await ReadAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
      throw new ModbusException($"Timeout occurred after {initialTimeout} milliseconds");
    }


    async Task<Memory<byte>> ReadAsync(CancellationToken ct)
    {
      return await Task.Run(async () =>
      {
        // 先设置到最后一位
        if (Stream.CanSeek) Stream.Seek(0, SeekOrigin.End);

        var buffer = new byte[length];
        var memory = buffer.AsMemory();
        var bytesRead = 0;

        // 是否已验证功能码
        var verificationFunctionCode = false;

        while (bytesRead < length)
        {
          var read = await Stream.ReadAsync(memory[bytesRead..], ct);

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

        // 验证数据帧
        VerifyFrame(result.Span);

        return result;
      }, ct);
    }
  }

  #endregion
}