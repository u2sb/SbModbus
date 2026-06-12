using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Sb.Extensions.System.Buffers.RingBuffers;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Utils;

namespace SbModbus.Services.ModbusServer;

/// <summary>
///   Modbus 帧解析器 — 从字节流中解析出完整的请求帧
/// </summary>
public static class FrameParser
{
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static ushort ReadU16BE(RingBufferSpan<byte> buf, int offset)
  {
    return (ushort)(buf[offset] << 8 | buf[offset + 1]);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static ushort ReadU16LE(RingBufferSpan<byte> buf, int offset)
  {
    return (ushort)(buf[offset] | buf[offset + 1] << 8);
  }

  /// <summary>
  ///   尝试解析 TCP/MBAP 帧（RingBufferSpan 重载，零拷贝）。
  ///   成功返回 true 并产出消息；失败返回 false（数据不足或格式错误）。
  /// </summary>
  public static bool TryParseTcp(IModbusStream session, ref RingBufferSpan<byte> buffer, out ModbusFrameMessage message)
  {
    message = default;

    if (buffer.Length < 6) return false; // MBAP 头

    var protocolId = ReadU16BE(buffer, 2);
    if (protocolId != 0)
    {
      Logger.Log(LogLevel.Error, $"TCP protocol identifier invalid: 0x{protocolId:X4}");
      buffer = default; // 清空，无法恢复
      return false;
    }

    var length = ReadU16BE(buffer, 4);

    // PDU 最小 2 字节（unitId+fc），最大 253 字节
    if (length is < 2 or > 253)
    {
      Logger.Log(LogLevel.Error, $"TCP PDU length out of range: {length}");
      buffer = default;
      return false;
    }

    var totalLength = 6 + length;

    if (buffer.Length < totalLength) return false; // PDU 不完整

    // 完整帧已就绪
    var frame = buffer[..totalLength];

    var tid = ReadU16LE(frame, 0);
    var pdu = frame.Slice(6, length);

    var unitId = pdu[0];
    var rawFc = pdu[1];
    var functionCode = (ModbusFunctionCode)(rawFc & 0x7F);
    var startingAddress = ReadU16BE(pdu, 2);

    ushort quantity = 0;
    var data = ReadOnlyMemory<byte>.Empty;

    switch (functionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        quantity = ReadU16BE(pdu, 4);
        break;

      case ModbusFunctionCode.WriteSingleCoil:
      case ModbusFunctionCode.WriteSingleRegister:
        quantity = 1;
        data = pdu.Slice(4, 2).ToArray();
        break;

      case ModbusFunctionCode.WriteMultipleCoils:
      case ModbusFunctionCode.WriteMultipleRegisters:
        quantity = ReadU16BE(pdu, 4);
        var byteCount = pdu[6];
        data = pdu.Slice(6, 1 + byteCount).ToArray();
        break;
    }

    message = new ModbusFrameMessage
    {
      Session = session,
      Tid = tid,
      UnitId = unitId,
      FunctionCode = functionCode,
      StartingAddress = startingAddress,
      Quantity = quantity,
      Data = data
    };

    // 消费已解析的帧
    buffer = buffer[totalLength..];
    return true;
  }

  /// <summary>
  ///   尝试解析 RTU/CRC 帧（RingBufferSpan 重载，零拷贝）。
  /// </summary>
  public static bool TryParseRtu(IModbusStream session, ref RingBufferSpan<byte> buffer, out ModbusFrameMessage message)
  {
    message = default;

    if (buffer.Length < 4) return false; // 帧头

    var slaveId = buffer[0];
    var rawFc = buffer[1];
    var functionCode = (ModbusFunctionCode)(rawFc & 0x7F);
    var startingAddress = ReadU16BE(buffer, 2);

    int totalLength;

    switch (functionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        totalLength = 4 + 2 + 2; // header + quantity(2) + CRC(2)
        break;

      case ModbusFunctionCode.WriteSingleCoil:
      case ModbusFunctionCode.WriteSingleRegister:
        totalLength = 4 + 2 + 2; // header + data(2) + CRC(2)
        break;

      case ModbusFunctionCode.WriteMultipleCoils:
      case ModbusFunctionCode.WriteMultipleRegisters:
        if (buffer.Length < 7) return false; // header + quantity(2) + byteCount(1)
        var qty = ReadU16BE(buffer, 4);
        var byteCnt = buffer[6];
        totalLength = 4 + 2 + 1 + byteCnt + 2; // header + quantity + byteCount + data + CRC
        break;

      default:
        totalLength = 4 + 2; // header + CRC，跳过不识别的帧
        break;
    }

    if (buffer.Length < totalLength) return false;

    // CRC 校验
    var frame = buffer[..totalLength];
    var crc = Crc16.CalculateCrc16(frame[..(totalLength - 2)]);
    var receivedCrc = ReadU16LE(frame, totalLength - 2);
    if (crc != receivedCrc)
    {
      Logger.Log(LogLevel.Error, $"RTU CRC error: calculated=0x{crc:X4}, received=0x{receivedCrc:X4}");
      buffer = buffer[totalLength..]; // 消费错误帧，丢弃
      return false;
    }

    ushort quantity = 0;
    var data = ReadOnlyMemory<byte>.Empty;

    switch (functionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        quantity = ReadU16BE(frame, 4);
        break;

      case ModbusFunctionCode.WriteSingleCoil:
      case ModbusFunctionCode.WriteSingleRegister:
        quantity = 1;
        data = frame.Slice(4, 2).ToArray();
        break;

      case ModbusFunctionCode.WriteMultipleCoils:
      case ModbusFunctionCode.WriteMultipleRegisters:
        quantity = ReadU16BE(frame, 4);
        var rtuByteCount = frame[6];
        data = frame.Slice(6, 1 + rtuByteCount).ToArray();
        break;
    }

    message = new ModbusFrameMessage
    {
      Session = session,
      Tid = 0,
      UnitId = slaveId,
      FunctionCode = functionCode,
      StartingAddress = startingAddress,
      Quantity = quantity,
      Data = data
    };

    buffer = buffer[totalLength..];
    return true;
  }

  /// <summary>
  ///   尝试解析 TCP/MBAP 帧（ReadOnlySpan 重载，向后兼容）。
  ///   成功返回 true 并产出消息；失败返回 false（数据不足或格式错误）。
  /// </summary>
  public static bool TryParseTcp(IModbusStream session, ref ReadOnlySpan<byte> buffer, out ModbusFrameMessage message)
  {
    message = default;

    if (buffer.Length < 6) return false; // MBAP 头

    var protocolId = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));
    if (protocolId != 0)
    {
      Logger.Log(LogLevel.Error, $"TCP protocol identifier invalid: 0x{protocolId:X4}");
      buffer = default; // 清空，无法恢复
      return false;
    }

    var length = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(4, 2));

    // PDU 最小 2 字节（unitId+fc），最大 253 字节
    if (length is < 2 or > 253)
    {
      Logger.Log(LogLevel.Error, $"TCP PDU length out of range: {length}");
      buffer = default;
      return false;
    }

    var totalLength = 6 + length;

    if (buffer.Length < totalLength) return false; // PDU 不完整

    // 完整帧已就绪
    var frame = buffer[..totalLength];

    var tid = BinaryPrimitives.ReadUInt16LittleEndian(frame[..2]);
    var pdu = frame.Slice(6, length);

    var unitId = pdu[0];
    var rawFc = pdu[1];
    var functionCode = (ModbusFunctionCode)(rawFc & 0x7F);
    var startingAddress = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(2, 2));

    ushort quantity = 0;
    var data = ReadOnlyMemory<byte>.Empty;

    switch (functionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(4, 2));
        break;

      case ModbusFunctionCode.WriteSingleCoil:
      case ModbusFunctionCode.WriteSingleRegister:
        quantity = 1;
        data = pdu.Slice(4, 2).ToArray();
        break;

      case ModbusFunctionCode.WriteMultipleCoils:
      case ModbusFunctionCode.WriteMultipleRegisters:
        quantity = BinaryPrimitives.ReadUInt16BigEndian(pdu.Slice(4, 2));
        var byteCount = pdu[6];
        data = pdu.Slice(6, 1 + byteCount).ToArray();
        break;
    }

    message = new ModbusFrameMessage
    {
      Session = session,
      Tid = tid,
      UnitId = unitId,
      FunctionCode = functionCode,
      StartingAddress = startingAddress,
      Quantity = quantity,
      Data = data
    };

    // 消费已解析的帧
    buffer = buffer[totalLength..];
    return true;
  }

  /// <summary>
  ///   尝试解析 RTU/CRC 帧（ReadOnlySpan 重载，向后兼容）。
  /// </summary>
  public static bool TryParseRtu(IModbusStream session, ref ReadOnlySpan<byte> buffer, out ModbusFrameMessage message)
  {
    message = default;

    if (buffer.Length < 4) return false; // 帧头

    var slaveId = buffer[0];
    var rawFc = buffer[1];
    var functionCode = (ModbusFunctionCode)(rawFc & 0x7F);
    var startingAddress = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(2, 2));

    int totalLength;

    switch (functionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        totalLength = 4 + 2 + 2; // header + quantity(2) + CRC(2)
        break;

      case ModbusFunctionCode.WriteSingleCoil:
      case ModbusFunctionCode.WriteSingleRegister:
        totalLength = 4 + 2 + 2; // header + data(2) + CRC(2)
        break;

      case ModbusFunctionCode.WriteMultipleCoils:
      case ModbusFunctionCode.WriteMultipleRegisters:
        if (buffer.Length < 7) return false; // header + quantity(2) + byteCount(1)
        var qty = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(4, 2));
        var byteCnt = buffer[6];
        totalLength = 4 + 2 + 1 + byteCnt + 2; // header + quantity + byteCount + data + CRC
        break;

      default:
        totalLength = 4 + 2; // header + CRC，跳过不识别的帧
        break;
    }

    if (buffer.Length < totalLength) return false;

    // CRC 校验
    var frame = buffer[..totalLength];
    var crc = Crc16.CalculateCrc16(frame[..(totalLength - 2)]);
    var receivedCrc = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(totalLength - 2, 2));
    if (crc != receivedCrc)
    {
      Logger.Log(LogLevel.Error, $"RTU CRC error: calculated=0x{crc:X4}, received=0x{receivedCrc:X4}");
      buffer = buffer[totalLength..]; // 消费错误帧，丢弃
      return false;
    }

    ushort quantity = 0;
    var data = ReadOnlyMemory<byte>.Empty;

    switch (functionCode)
    {
      case ModbusFunctionCode.ReadCoils:
      case ModbusFunctionCode.ReadDiscreteInputs:
      case ModbusFunctionCode.ReadHoldingRegisters:
      case ModbusFunctionCode.ReadInputRegisters:
        quantity = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(4, 2));
        break;

      case ModbusFunctionCode.WriteSingleCoil:
      case ModbusFunctionCode.WriteSingleRegister:
        quantity = 1;
        data = frame.Slice(4, 2).ToArray();
        break;

      case ModbusFunctionCode.WriteMultipleCoils:
      case ModbusFunctionCode.WriteMultipleRegisters:
        quantity = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(4, 2));
        var rtuByteCount = frame[6];
        data = frame.Slice(6, 1 + rtuByteCount).ToArray();
        break;
    }

    message = new ModbusFrameMessage
    {
      Session = session,
      Tid = 0,
      UnitId = slaveId,
      FunctionCode = functionCode,
      StartingAddress = startingAddress,
      Quantity = quantity,
      Data = data
    };

    buffer = buffer[totalLength..];
    return true;
  }
}
