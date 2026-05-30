using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using Sb.Extensions.System;
using SbModbus.Models;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusClient;

public partial class ModbusTcpClient
{
  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    Logger.Log(LogLevel.Debug, $"ReadCoils: unit={unitIdentifier}, address={startingAddress}, count={count}");
    using var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).WithEndianness(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    ((ushort)(buffer.WrittenCount - 6)).WriteTo(buffer.RawBuffer.AsSpan(4, 2), BigAndSmallEndianEncodingMode.ABCD);
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress,
    int count, CancellationToken ct = default)
  {
    Logger.Log(LogLevel.Debug, $"ReadDiscreteInputs: unit={unitIdentifier}, address={startingAddress}, count={count}");
    using var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress,
      ConvertUshort(count).WithEndianness(true));

    // 7MBAP 1功能码 1数据长度 (n +7) / 8数据
    var length = 7 + 1 + 1 + ((count + 7) >> 3);
    ((ushort)(buffer.WrittenCount - 6)).WriteTo(buffer.RawBuffer.AsSpan(4, 2), BigAndSmallEndianEncodingMode.ABCD);
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data,
    CancellationToken ct = default)
  {
    ValidateSingleCoilData(data);
    Logger.Log(LogLevel.Debug, $"WriteSingleCoil: unit={unitIdentifier}, address={startingAddress}");
    using var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress, data.Span);

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    ((ushort)(buffer.WrittenCount - 6)).WriteTo(buffer.RawBuffer.AsSpan(4, 2), BigAndSmallEndianEncodingMode.ABCD);
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data,
    CancellationToken ct = default)
  {
    Logger.Log(LogLevel.Debug, $"WriteSingleRegister: unit={unitIdentifier}, address={startingAddress}");
    using var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress, data.Span);

    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    ((ushort)(buffer.WrittenCount - 6)).WriteTo(buffer.RawBuffer.AsSpan(4, 2), BigAndSmallEndianEncodingMode.ABCD);
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public override async ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct = default)
  {
    var l = data.Length;
    Logger.Log(LogLevel.Debug, $"WriteMultipleRegisters: unit={unitIdentifier}, address={startingAddress}, registers={l / 2}");
    using var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress, data.Span,
      writer =>
      {
        // 写寄存器数量
        writer.Write(ConvertUshort(l / 2).WithEndianness(true));

        // 写字节数
        writer.Write(ConvertByte(l));
      });


    // 7MBAP 1功能码 2寄存器地址 2数据数量
    const int length = 7 + 1 + 2 + 2;
    ((ushort)(buffer.WrittenCount - 6)).WriteTo(buffer.RawBuffer.AsSpan(4, 2), BigAndSmallEndianEncodingMode.ABCD);

    await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);
  }

  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> ReadRegistersAsync(int unitIdentifier,
    ModbusFunctionCode functionCode,
    int startingAddress, int count, CancellationToken ct = default)
  {
    Logger.Log(LogLevel.Debug, $"ReadRegisters: unit={unitIdentifier}, function=0x{(int)functionCode:X2}, address={startingAddress}, count={count}");
    using var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ConvertUshort(count).WithEndianness(true));

    // 7MBAP 1功能码 1数据长度 2n数据
    var length = 7 + 1 + 1 + count * 2;
    ((ushort)(buffer.WrittenCount - 6)).WriteTo(buffer.RawBuffer.AsSpan(4, 2), BigAndSmallEndianEncodingMode.ABCD);
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);

    // 返回数据
    return result[9..];
  }

  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data, int length,
    int readTimeout, CancellationToken ct = default)
  {
    if (!ModbusStream.IsConnected) SbModbusThrow.NotConnected();
    if (readTimeout <= 0) throw new SbModbusException("ReadTimeout must be greater than 0.");

    var tid = data.Span[..2].ToUInt16();
    var expectedUnitId = data.Span[6];
    var expectedFunctionCode = (ModbusFunctionCode)data.Span[7];

    using var mt = await ModbusStream.LockAsync(ct).ConfigureAwait(false);
    // 清除读缓存
    await mt.ClearReadBufferAsync(ct).ConfigureAwait(false);

    // 写入数据
    await mt.WriteAsync(data, ct).ConfigureAwait(false);

    await DataSentAsync(data);

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
        var read = await mt.ReadAsync(memory[bytesRead..], cts.Token).ConfigureAwait(false);

        // 如果没读到数据就跳过
        if (read > 0)
        {
          bytesRead += read;

          // 如果已经读到功能码和错误码
          if (!verificationFunctionCode && bytesRead >= 9)
          {
            // 如果功能码不一致
            if ((buffer[7] & 0x80) != 0)
            {
              length = 9; // 响应不正常时返回值长度变为9
              Logger.Log(LogLevel.Warning, $"TCP exception response detected, adjusting expected length to 9, bytesRead={bytesRead}");
            }
            verificationFunctionCode = true;
          }
        }
        else
        {
          await Task.Yield();
        }
      }

      var result = memory[..length];

      VerifyFrame(result.Span, tid, expectedUnitId, expectedFunctionCode);

      await DataReceivedAsync(result);

      return result;
    }
    catch (OperationCanceledException)
    {
      // 用户取消 vs 超时
      if (ct.IsCancellationRequested)
      {
        Logger.Log(LogLevel.Debug, $"TCP operation cancelled by user, tid={tid}");
        throw;
      }

      Logger.Log(LogLevel.Error, $"TCP read timeout after {readTimeout}ms, tid={tid}, unit={expectedUnitId}, function=0x{(int)expectedFunctionCode:X2}");
      SbModbusThrow.Timeout(readTimeout);
      // unreachable
      return default;
    }
    catch (EndOfStreamException)
    {
      Logger.Log(LogLevel.Error, $"TCP end of stream, tid={tid}, unit={expectedUnitId}, function=0x{(int)expectedFunctionCode:X2}");
      SbModbusThrow.Timeout(readTimeout);
      // unreachable
      return default;
    }
    catch (SbModbusException)
    {
      throw;
    }
    catch (Exception ex)
    {
      Logger.Error(ex, $"Unexpected error during TCP communication, tid={tid}, unit={expectedUnitId}, function=0x{(int)expectedFunctionCode:X2}");
      throw new SbModbusException("An unexpected error occurred during TCP communication.", ex);
    }
  }
}
