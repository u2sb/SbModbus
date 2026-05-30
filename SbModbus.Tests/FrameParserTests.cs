using System.Buffers.Binary;
using SbModbus.Models;
using SbModbus.Services.ModbusServer;

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
