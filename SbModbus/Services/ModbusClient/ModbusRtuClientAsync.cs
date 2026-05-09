using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Sb.Extensions.System;
using SbModbus.Models;

namespace SbModbus.Services.ModbusClient;

public partial class ModbusRtuClient
{
  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadCoilsAsync(int unitIdentifier, int startingAddress, int count,
    CancellationToken ct = default)
  {
    logger?.LogDebug("ReadCoils: slave={SlaveId}, address={Address}, count={Count}", unitIdentifier, startingAddress, count);
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadCoils, startingAddress,
      ConvertUshort(count).WithEndianness(true));

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct)
      .ConfigureAwait(false);

    // 返回数据
    return result[3..^2];
  }

  /// <inheritdoc />
  public override async ValueTask<Memory<byte>> ReadDiscreteInputsAsync(int unitIdentifier, int startingAddress,
    int count, CancellationToken ct = default)
  {
    logger?.LogDebug("ReadDiscreteInputs: slave={SlaveId}, address={Address}, count={Count}", unitIdentifier, startingAddress, count);
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.ReadDiscreteInputs, startingAddress,
      ConvertUshort(count).WithEndianness(true));

    // 1地址 1功能码 1数据长度 (n +7) / 8数据 2校验
    var length = 1 + 1 + 1 + ((count + 7) >> 3) + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct)
      .ConfigureAwait(false);

    // 返回数据
    return result[3..^2];
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleCoilAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data,
    CancellationToken ct = default)
  {
    ValidateSingleCoilData(data);
    logger?.LogDebug("WriteSingleCoil: slave={SlaveId}, address={Address}", unitIdentifier, startingAddress);
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleCoil, startingAddress, data.Span);

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public override async ValueTask WriteSingleRegisterAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct = default)
  {
    logger?.LogDebug("WriteSingleRegister: slave={SlaveId}, address={Address}", unitIdentifier, startingAddress);
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteSingleRegister, startingAddress, data.Span);

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);
  }

  /// <inheritdoc />
  public override async ValueTask WriteMultipleRegistersAsync(int unitIdentifier, int startingAddress,
    ReadOnlyMemory<byte> data, CancellationToken ct = default)
  {
    var l = data.Length;
    logger?.LogDebug("WriteMultipleRegisters: slave={SlaveId}, address={Address}, registers={RegisterCount}", unitIdentifier, startingAddress, l / 2);
    var buffer = CreateFrame(unitIdentifier, ModbusFunctionCode.WriteMultipleRegisters, startingAddress, data.Span,
      writer =>
      {
        // 写寄存器数量
        writer.Write(ConvertUshort(l / 2).WithEndianness(true));

        // 写字节数
        writer.Write(ConvertByte(l));
      });

    // 1设备地址 1功能码 2寄存器地址 2数据数量 2校验
    const int length = 1 + 1 + 2 + 2 + 2;
    _ = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct).ConfigureAwait(false);
  }

  #region 通用方法

  /// <exception cref="SbModbusException"></exception>
  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> WriteAndReadWithTimeoutAsync(ReadOnlyMemory<byte> data,
    int length,
    int readTimeout, CancellationToken ct = default)
  {
    if (!ModbusStream.IsConnected) SbModbusThrow.NotConnected();
    if (readTimeout <= 0) throw new SbModbusException("ReadTimeout must be greater than 0.");

    var expectedSlaveId = data.Span[0];
    var expectedFunctionCode = (ModbusFunctionCode)data.Span[1];
    using var mt = await ModbusStream.LockAsync(ct).ConfigureAwait(false);

    await WaitDiffSidIntervalTimeAsync(data.Span[0]).ConfigureAwait(false);

    // 清除读缓存
    await mt.ClearReadBufferAsync(ct).ConfigureAwait(false);

    // 写入数据
    await mt.WriteAsync(data, ct).ConfigureAwait(false);

    await DataSentAsync(data);

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
        var read = await mt.ReadAsync(memory[bytesRead..], cts.Token).ConfigureAwait(false);

        // 如果没读到数据就跳过
        if (read > 0)
        {
          bytesRead += read;

          // 如果已经读到功能码和错误码
          if (!verificationFunctionCode && bytesRead >= 2)
          {
            // 如果功能码不一致
            if ((buffer[1] & 0x80) != 0)
            {
              length = 5; // 响应不正常时返回值长度变为5
              logger?.LogWarning("RTU exception response detected, adjusting expected length to 5, bytesRead={BytesRead}", bytesRead);
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

      // 验证数据帧
      VerifyFrame(result.Span, expectedSlaveId, expectedFunctionCode);

      await DataReceivedAsync(result);

      return result;
    }
    catch (OperationCanceledException)
    {
      // 用户取消 vs 超时
      if (ct.IsCancellationRequested)
      {
        logger?.LogDebug("RTU operation cancelled by user, slave={SlaveId}", expectedSlaveId);
        throw;
      }

      logger?.LogError("RTU read timeout after {Timeout}ms, slave={SlaveId}, function=0x{FunctionCode:X2}", readTimeout, expectedSlaveId, (int)expectedFunctionCode);
      SbModbusThrow.Timeout(readTimeout);
      // unreachable
      return default;
    }
    catch (EndOfStreamException)
    {
      logger?.LogError("RTU end of stream, slave={SlaveId}, function=0x{FunctionCode:X2}", expectedSlaveId, (int)expectedFunctionCode);
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
      logger?.LogError(ex, "Unexpected error during RTU communication, slave={SlaveId}, function=0x{FunctionCode:X2}", expectedSlaveId, (int)expectedFunctionCode);
      throw new SbModbusException("An unexpected error occurred during RTU communication.", ex);
    }
    finally
    {
      SetPreviousOverTime(data.Span[0]);
    }
  }

  /// <inheritdoc />
  protected override async ValueTask<Memory<byte>> ReadRegistersAsync(int unitIdentifier,
    ModbusFunctionCode functionCode, int startingAddress, int count, CancellationToken ct = default)
  {
    logger?.LogDebug("ReadRegisters: slave={SlaveId}, function=0x{FunctionCode:X2}, address={Address}, count={Count}", unitIdentifier, (int)functionCode, startingAddress, count);
    var buffer = CreateFrame(unitIdentifier, functionCode, startingAddress, ReadOnlySpan<byte>.Empty, writer =>
    {
      // 写长度
      writer.Write(ConvertUshort(count).WithEndianness(true));
    });


    // 1设备地址 1功能码 1数据长度 2n数据 2校验
    var length = 1 + 1 + 1 + count * 2 + 2;
    var result = await WriteAndReadWithTimeoutAsync(buffer.WrittenMemory, length, ReadTimeout, ct)
      .ConfigureAwait(false);

    // 返回数据
    return result[3..^2];
  }

  #endregion
}
