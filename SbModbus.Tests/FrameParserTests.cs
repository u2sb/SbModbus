using System.Buffers.Binary;
using SbModbus.Models;
using SbModbus.Services.ModbusServer;
using SbModbus.Utils;

namespace SbModbus.Tests;

public class FrameParserTests
{
  private static byte[] BuildTcpReadCoilsFrame(ushort tid, byte unitId, ushort addr, ushort count)
  {
    var frame = new byte[12];
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), tid);
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), 6);
    frame[6] = unitId;
    frame[7] = (byte)ModbusFunctionCode.ReadCoils;
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(8, 2), addr);
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(10, 2), count);
    return frame;
  }

  #region RTU 帧构建辅助方法

  /// <summary>构建 RTU 读请求帧（FC01/FC02/FC03/FC04）</summary>
  private static byte[] BuildRtuReadFrame(byte slaveId, ModbusFunctionCode fc, ushort addr, ushort count)
  {
    var frame = new byte[8];
    frame[0] = slaveId;
    frame[1] = (byte)fc;
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), addr);
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), count);
    var crc = Crc16.CalculateCrc16(frame.AsSpan(0, 6));
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), crc);
    return frame;
  }

  /// <summary>构建 RTU 写单线圈/寄存器帧（FC05/FC06）</summary>
  private static byte[] BuildRtuWriteSingleFrame(byte slaveId, ModbusFunctionCode fc, ushort addr, byte[] data)
  {
    var frame = new byte[8];
    frame[0] = slaveId;
    frame[1] = (byte)fc;
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), addr);
    frame[4] = data[0];
    frame[5] = data[1];
    var crc = Crc16.CalculateCrc16(frame.AsSpan(0, 6));
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), crc);
    return frame;
  }

  /// <summary>构建 RTU 写多个线圈/寄存器帧（FC15/FC16）</summary>
  private static byte[] BuildRtuWriteMultipleFrame(byte slaveId, ModbusFunctionCode fc, ushort addr, ushort qty, byte[] data)
  {
    // header(7) + data + CRC(2)
    var frame = new byte[9 + data.Length];
    frame[0] = slaveId;
    frame[1] = (byte)fc;
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), addr);
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(4, 2), qty);
    frame[6] = (byte)data.Length;
    Array.Copy(data, 0, frame, 7, data.Length);
    var crc = Crc16.CalculateCrc16(frame.AsSpan(0, 7 + data.Length));
    BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7 + data.Length, 2), crc);
    return frame;
  }

  #endregion

  [Fact]
  public void ParseTwoFramesInOneChunk_BothParsed()
  {
    var f1 = BuildTcpReadCoilsFrame(1, 1, 0, 8);
    var f2 = BuildTcpReadCoilsFrame(2, 1, 10, 16);
    var combined = new byte[f1.Length + f2.Length];
    f1.CopyTo(combined, 0);
    f2.CopyTo(combined, f1.Length);

    ReadOnlySpan<byte> buffer = combined;
    var session = new FakeStream();
    var parsed = new List<ModbusFrameMessage>();
    while (FrameParser.TryParseTcp(session, ref buffer, out var msg))
      parsed.Add(msg);

    Assert.Equal(2, parsed.Count);
    Assert.Equal((ushort)1, parsed[0].Tid);
    Assert.Equal((ushort)0, parsed[0].StartingAddress);
    Assert.Equal((ushort)8, parsed[0].Quantity);
    Assert.Equal((ushort)2, parsed[1].Tid);
    Assert.Equal((ushort)10, parsed[1].StartingAddress);
    Assert.Equal((ushort)16, parsed[1].Quantity);
    Assert.True(buffer.IsEmpty);
  }

  [Fact]
  public void ParsePartialThenRest_AccumulatedCorrectly()
  {
    var f1 = BuildTcpReadCoilsFrame(1, 1, 0, 8);

    var accum = new List<byte>();
    accum.AddRange(f1.AsSpan(0, 10));
    ReadOnlySpan<byte> buf = accum.ToArray();
    var session = new FakeStream();
    var parsed = 0;
    while (FrameParser.TryParseTcp(session, ref buf, out var _)) parsed++;
    Assert.Equal(0, parsed);
    accum.Clear();
    if (!buf.IsEmpty) accum.AddRange(buf.ToArray());
    Assert.Equal(10, accum.Count);

    accum.AddRange(f1.AsSpan(10, 2));
    buf = accum.ToArray();
    while (FrameParser.TryParseTcp(session, ref buf, out var _)) parsed++;
    Assert.Equal(1, parsed);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void ParseFrames_MultipleAccumulated_AllParsed()
  {
    var f1 = BuildTcpReadCoilsFrame(1, 1, 0, 4);
    var f2 = BuildTcpReadCoilsFrame(2, 1, 10, 4);
    var f3 = BuildTcpReadCoilsFrame(3, 1, 100, 4);

    var accum = new List<byte>();
    accum.AddRange(f1);
    accum.AddRange(f2.AsSpan(0, 8));

    ReadOnlySpan<byte> buf = accum.ToArray();
    var session = new FakeStream();
    var count = 0;
    while (FrameParser.TryParseTcp(session, ref buf, out var _)) count++;
    Assert.Equal(1, count);
    accum.Clear();
    if (!buf.IsEmpty) accum.AddRange(buf.ToArray());
    Assert.True(accum.Count > 0);

    accum.AddRange(f2.AsSpan(8, 4));
    accum.AddRange(f3);
    buf = accum.ToArray();
    while (FrameParser.TryParseTcp(session, ref buf, out var _)) count++;
    Assert.Equal(3, count);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void InvalidProtocolId_ClearsBuffer()
  {
    var frame = BuildTcpReadCoilsFrame(1, 1, 0, 8);
    BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), 0x0001);
    ReadOnlySpan<byte> buf = frame;
    Assert.False(FrameParser.TryParseTcp(new FakeStream(), ref buf, out _));
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void EmptyBuffer_ReturnsFalse()
  {
    ReadOnlySpan<byte> buf = [];
    Assert.False(FrameParser.TryParseTcp(new FakeStream(), ref buf, out _));
  }

  [Fact]
  public void BufferSmallerThanHeader_ReturnsFalse_KeepsData()
  {
    ReadOnlySpan<byte> buf = new byte[4];
    var session = new FakeStream();
    Assert.False(FrameParser.TryParseTcp(session, ref buf, out _));
    Assert.Equal(4, buf.Length);
  }

  #region RTU 帧解析测试

  [Fact]
  public void Rtu_ParseSingleFrame_Success()
  {
    var frame = BuildRtuReadFrame(1, ModbusFunctionCode.ReadCoils, 0, 8);
    ReadOnlySpan<byte> buf = frame;
    var session = new FakeStream();
    Assert.True(FrameParser.TryParseRtu(session, ref buf, out var msg));
    Assert.Equal((byte)1, msg.UnitId);
    Assert.Equal(ModbusFunctionCode.ReadCoils, msg.FunctionCode);
    Assert.Equal((ushort)0, msg.StartingAddress);
    Assert.Equal((ushort)8, msg.Quantity);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void Rtu_ParseTwoFramesInOneChunk_BothParsed()
  {
    var f1 = BuildRtuReadFrame(1, ModbusFunctionCode.ReadHoldingRegisters, 0, 8);
    var f2 = BuildRtuReadFrame(2, ModbusFunctionCode.ReadInputRegisters, 10, 4);
    var combined = new byte[f1.Length + f2.Length];
    f1.CopyTo(combined, 0);
    f2.CopyTo(combined, f1.Length);

    ReadOnlySpan<byte> buffer = combined;
    var session = new FakeStream();
    var parsed = new List<ModbusFrameMessage>();
    while (FrameParser.TryParseRtu(session, ref buffer, out var msg))
      parsed.Add(msg);

    Assert.Equal(2, parsed.Count);
    Assert.Equal((byte)1, parsed[0].UnitId);
    Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, parsed[0].FunctionCode);
    Assert.Equal((byte)2, parsed[1].UnitId);
    Assert.Equal(ModbusFunctionCode.ReadInputRegisters, parsed[1].FunctionCode);
    Assert.Equal((ushort)10, parsed[1].StartingAddress);
    Assert.True(buffer.IsEmpty);
  }

  [Fact]
  public void Rtu_ParsePartialThenRest_AccumulatedCorrectly()
  {
    var f1 = BuildRtuReadFrame(1, ModbusFunctionCode.ReadCoils, 0, 8);

    // 先喂入前半部分（不足一帧）
    var accum = new List<byte>();
    accum.AddRange(f1.AsSpan(0, 5)); // slaveId + fc + addr(2) + 1 byte of quantity
    ReadOnlySpan<byte> buf = accum.ToArray();
    var session = new FakeStream();
    var parsed = 0;
    while (FrameParser.TryParseRtu(session, ref buf, out var _)) parsed++;
    Assert.Equal(0, parsed);
    accum.Clear();
    if (!buf.IsEmpty) accum.AddRange(buf.ToArray());
    Assert.Equal(5, accum.Count);

    // 喂入后半部分，完成帧
    accum.AddRange(f1.AsSpan(5, 3));
    buf = accum.ToArray();
    while (FrameParser.TryParseRtu(session, ref buf, out var _)) parsed++;
    Assert.Equal(1, parsed);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void Rtu_ParseFrames_MultipleAccumulated_AllParsed()
  {
    var f1 = BuildRtuReadFrame(1, ModbusFunctionCode.ReadCoils, 0, 4);
    var f2 = BuildRtuReadFrame(1, ModbusFunctionCode.ReadDiscreteInputs, 10, 4);
    var f3 = BuildRtuReadFrame(1, ModbusFunctionCode.ReadHoldingRegisters, 100, 4);

    var accum = new List<byte>();
    accum.AddRange(f1);
    accum.AddRange(f2.AsSpan(0, 5)); // 半帧

    ReadOnlySpan<byte> buf = accum.ToArray();
    var session = new FakeStream();
    var count = 0;
    while (FrameParser.TryParseRtu(session, ref buf, out var _)) count++;
    Assert.Equal(1, count);
    accum.Clear();
    if (!buf.IsEmpty) accum.AddRange(buf.ToArray());
    Assert.True(accum.Count > 0);

    accum.AddRange(f2.AsSpan(5, 3)); // 补全 f2
    accum.AddRange(f3);
    buf = accum.ToArray();
    while (FrameParser.TryParseRtu(session, ref buf, out var _)) count++;
    Assert.Equal(3, count);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void Rtu_WriteSingleCoil_Success()
  {
    var frame = BuildRtuWriteSingleFrame(1, ModbusFunctionCode.WriteSingleCoil, 10, [0xFF, 0x00]);
    ReadOnlySpan<byte> buf = frame;
    var session = new FakeStream();
    Assert.True(FrameParser.TryParseRtu(session, ref buf, out var msg));
    Assert.Equal(ModbusFunctionCode.WriteSingleCoil, msg.FunctionCode);
    Assert.Equal((ushort)10, msg.StartingAddress);
    Assert.Equal((ushort)1, msg.Quantity);
    Assert.Equal(0xFF, msg.Data.Span[0]);
    Assert.Equal(0x00, msg.Data.Span[1]);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void Rtu_WriteSingleRegister_Success()
  {
    var frame = BuildRtuWriteSingleFrame(3, ModbusFunctionCode.WriteSingleRegister, 30, [0x12, 0x34]);
    ReadOnlySpan<byte> buf = frame;
    var session = new FakeStream();
    Assert.True(FrameParser.TryParseRtu(session, ref buf, out var msg));
    Assert.Equal(ModbusFunctionCode.WriteSingleRegister, msg.FunctionCode);
    Assert.Equal((byte)3, msg.UnitId);
    Assert.Equal((ushort)30, msg.StartingAddress);
    Assert.Equal(0x12, msg.Data.Span[0]);
    Assert.Equal(0x34, msg.Data.Span[1]);
  }

  [Fact]
  public void Rtu_WriteMultipleRegisters_Success()
  {
    var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
    var frame = BuildRtuWriteMultipleFrame(1, ModbusFunctionCode.WriteMultipleRegisters, 40, 2, data);
    ReadOnlySpan<byte> buf = frame;
    var session = new FakeStream();
    Assert.True(FrameParser.TryParseRtu(session, ref buf, out var msg));
    Assert.Equal(ModbusFunctionCode.WriteMultipleRegisters, msg.FunctionCode);
    Assert.Equal((ushort)40, msg.StartingAddress);
    Assert.Equal((ushort)2, msg.Quantity);
    Assert.Equal(4, msg.Data.Span[1..].Length); // data = byteCount + payload
    Assert.Equal(0xAA, msg.Data.Span[1]);
    Assert.True(buf.IsEmpty);
  }

  [Fact]
  public void Rtu_WriteMultipleCoils_Success()
  {
    var data = new byte[] { 0b_0000_1101 };
    var frame = BuildRtuWriteMultipleFrame(2, ModbusFunctionCode.WriteMultipleCoils, 100, 5, data);
    ReadOnlySpan<byte> buf = frame;
    var session = new FakeStream();
    Assert.True(FrameParser.TryParseRtu(session, ref buf, out var msg));
    Assert.Equal(ModbusFunctionCode.WriteMultipleCoils, msg.FunctionCode);
    Assert.Equal((ushort)100, msg.StartingAddress);
    Assert.Equal((ushort)5, msg.Quantity);
    Assert.Equal(0b_0000_1101, msg.Data.Span[1]);
  }

  [Fact]
  public void Rtu_BufferSmallerThanHeader_ReturnsFalse_KeepsData()
  {
    ReadOnlySpan<byte> buf = new byte[3]; // 小于 RTU 最小帧头 4 字节
    var session = new FakeStream();
    Assert.False(FrameParser.TryParseRtu(session, ref buf, out _));
    Assert.Equal(3, buf.Length);
  }

  [Fact]
  public void Rtu_EmptyBuffer_ReturnsFalse()
  {
    ReadOnlySpan<byte> buf = [];
    Assert.False(FrameParser.TryParseRtu(new FakeStream(), ref buf, out _));
  }

  [Fact]
  public void Rtu_CrcMismatch_DiscardsFrame_ConsumesBuffer()
  {
    var frame = BuildRtuReadFrame(1, ModbusFunctionCode.ReadCoils, 0, 8);
    // 破坏 CRC：翻转第一个 CRC 字节
    frame[6] ^= 0xFF;
    ReadOnlySpan<byte> buf = frame;
    Assert.False(FrameParser.TryParseRtu(new FakeStream(), ref buf, out _));
    Assert.True(buf.IsEmpty); // 错误帧被消费
  }

  #endregion

  private sealed class FakeStream : IModbusStream
  {
    public bool IsDisposed => false;
    public bool IsConnected => true;
    public bool AutoReceive { get; set; }
    public int ReadTimeout { get; set; }
    public int WriteTimeout { get; set; }
    public event ModbusStreamDataHandler? OnDataReceived;
    public event ModbusStreamDataHandler? OnDataSent;
    public event Action<IModbusStream, bool>? OnConnectStateChanged;
    public bool Connect() => true;
    public bool Disconnect() => true;
    public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    public Task<bool> DisconnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    public ModbusStream.LockedModbusStream Lock() => throw new NotImplementedException();
    public Task<ModbusStream.LockedModbusStream> LockAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public string GetTransportInfo() => "fake";
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
