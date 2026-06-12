using System.Net;
using SbModbus.Protocol;
using SbModbus.Transport;
using SbModbus.Services.ModbusClient;
using SbModbus.Services.ModbusServer;
using SbModbus.TcpStream;

namespace SbModbus.Tests;

public class ModbusTcpE2ETests : IAsyncLifetime
{
  private ModbusTcpClient _client = null!;
  private SbTcpClientStream _clientStream = null!;
  private int _port;
  private ModbusTcpServer _server = null!;
  private SbTcpStreamServer _streamServer = null!;

  public async ValueTask InitializeAsync()
  {
    var probeStream = new SbTcpStreamServer(IPAddress.Loopback, 0);
    probeStream.Start();
    _port = probeStream.LocalEndPoint!.Port;
    probeStream.Stop();
    probeStream.Dispose();
    await Task.Delay(100);

    _streamServer = new SbTcpStreamServer(IPAddress.Loopback, _port);
    _server = new ModbusTcpServer();
    _server.UnitIdentifier.Add(1);
    _ = _server.StartAsync(_streamServer);
    await Task.Delay(300);

    _clientStream = new SbTcpClientStream(IPAddress.Loopback, _port);
    _client = new ModbusTcpClient(_clientStream);
    await _clientStream.ConnectAsync();
  }

  public async ValueTask DisposeAsync()
  {
    // 先停止服务端，再断开客户端
    try
    {
      await _server.StopAsync();
      _streamServer.Stop();
      _streamServer.Dispose();
      _server.Dispose();
    }
    catch
    {
    }

    try
    {
      await _clientStream.DisconnectAsync();
      _clientStream.Dispose();
      _client.Dispose();
    }
    catch
    {
    }
  }

  [Fact]
  public async Task ReadCoils_ReturnsCorrectValues()
  {
    // 使用 block scope 确保 lock 在 client 调用前释放
    {
      using var coils = await _server.Coils.LockAsync();
      coils.Data.Set(0, true);
      coils.Data.Set(1, false);
      coils.Data.Set(2, true);
      coils.Data.Set(7, true);
    }

    var result = await _client.ReadCoilsAsync(1, 0, 8);
    Assert.Equal(1, result.Length);
    Assert.Equal(0x85, result.Span[0]);
  }

  [Fact]
  public async Task ReadHoldingRegisters_ReturnsCorrectValues()
  {
    {
      using var reg = await _server.HoldingRegisters.LockAsync();
      // 原样写入原始字节（不做字节序转换）
      reg.Data[0] = 0x12;
      reg.Data[1] = 0x34;
      reg.Data[2] = 0x56;
      reg.Data[3] = 0x78;
      reg.Data[4] = 0xAB;
      reg.Data[5] = 0xCD;
    }

    var result = await _client.ReadHoldingRegistersAsync(1, 0, 3);
    Assert.Equal(6, result.Length);
    Assert.Equal(0x12, result.Span[0]);
    Assert.Equal(0x34, result.Span[1]);
    Assert.Equal(0xAB, result.Span[4]);
    Assert.Equal(0xCD, result.Span[5]);
  }

  [Fact]
  public async Task WriteSingleCoil_FiresHandler()
  {
    var handlerFired = new TaskCompletionSource<bool>();
    var handler = new ModbusCoilsWriteHandler(3, 5, _ => handlerFired.TrySetResult(true));
    _server.AddHandler(handler);

    await _client.WriteSingleCoilAsync(1, 5, true);
    Assert.True(await handlerFired.Task.WaitAsync(TimeSpan.FromSeconds(5)));
  }

  [Fact]
  public async Task WriteMultipleRegisters_FiresHandler()
  {
    var handlerFired = new TaskCompletionSource<bool>();
    var handler = new ModbusRegistersWriteHandler(20, 10, _ => handlerFired.TrySetResult(true));
    _server.AddHandler(handler);

    await _client.WriteMultipleRegistersAsync(1, 22, new byte[]
    {
      0x11, 0x11, 0x22, 0x22
    });
    Assert.True(await handlerFired.Task.WaitAsync(TimeSpan.FromSeconds(5)));
  }

  [Fact]
  public async Task WriteSingleCoil_ReadBack_ReturnsCorrectValue()
  {
    await _client.WriteSingleCoilAsync(1, 9, true);
    var result = await _client.ReadCoilsAsync(1, 8, 2);
    Assert.Equal(0x02, result.Span[0]);
  }

  [Fact]
  public async Task WriteMultipleRegisters_ReadBack_ReturnsCorrectValues()
  {
    var data = new byte[]
    {
      0xAA, 0xBB, 0xCC, 0xDD
    };
    await _client.WriteMultipleRegistersAsync(1, 30, data);

    var result = await _client.ReadHoldingRegistersAsync(1, 30, 2);
    Assert.Equal(0xAA, result.Span[0]);
    Assert.Equal(0xBB, result.Span[1]);
    Assert.Equal(0xCC, result.Span[2]);
    Assert.Equal(0xDD, result.Span[3]);
  }

  [Fact]
  public async Task InvalidUnitId_ThrowsTimeout()
  {
    // Server 静默忽略不匹配的站号，Client 等待超时
    var ex = await Assert.ThrowsAsync<SbModbusException>(() =>
      _client.ReadHoldingRegistersAsync(99, 0, 1).AsTask());
    Assert.Contains("Timeout", ex.Message);
  }

  [Fact]
  public async Task TwoClients_ReadSameCoils()
  {
    {
      using var coils = await _server.Coils.LockAsync();
      coils.Data.Set(0, true);
    }

    using var stream2 = new SbTcpClientStream(IPAddress.Loopback, _port);
    using var client2 = new ModbusTcpClient(stream2);
    await stream2.ConnectAsync();

    var r1 = await _client.ReadCoilsAsync(1, 0, 1);
    var r2 = await client2.ReadCoilsAsync(1, 0, 1);
    Assert.Equal(0x01, r1.Span[0]);
    Assert.Equal(r1.Span[0], r2.Span[0]);

    await stream2.DisconnectAsync();
  }

  [Fact]
  public async Task SendTwoRequestsInQuickSuccession_BothHandlersFire()
  {
    var fired1 = new TaskCompletionSource<bool>();
    var fired2 = new TaskCompletionSource<bool>();
    _server.AddHandler(new ModbusCoilsWriteHandler(0, 10, _ => fired1.TrySetResult(true)));
    _server.AddHandler(new ModbusRegistersWriteHandler(50, 10, _ => fired2.TrySetResult(true)));

    var t1 = _client.WriteSingleCoilAsync(1, 3, true).AsTask();
    var t2 = _client.WriteSingleRegisterAsync(1, 55, new byte[]
    {
      0x12, 0x34
    }).AsTask();

    await Task.WhenAll(t1, t2);

    Assert.True(await fired1.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    Assert.True(await fired2.Task.WaitAsync(TimeSpan.FromSeconds(5)));
  }

  /// <summary>
  ///   发送多种不同类型的 Modbus 请求，验证服务端能正确解码每种功能码，
  ///   同时验证客户端能正确解码服务端返回的各类响应。
  /// </summary>
  [Fact]
  public async Task SendVariousFunctionCodes_ServerDecodesAll_ClientDecodesResponses()
  {
    // ---- 准备服务端数据 ----
    {
      using var coils = await _server.Coils.LockAsync();
      coils.Data.Set(0, true);
      coils.Data.Set(1, false);
      coils.Data.Set(2, true);
      coils.Data.Set(3, true);
    }
    {
      using var di = await _server.DiscreteInputs.LockAsync();
      di.Data.Set(5, true);
      di.Data.Set(6, false);
      di.Data.Set(7, true);
    }
    {
      using var hr = await _server.HoldingRegisters.LockAsync();
      hr.Data[0] = 0x12;
      hr.Data[1] = 0x34;
      hr.Data[2] = 0xAB;
      hr.Data[3] = 0xCD;
    }
    {
      using var ir = await _server.InputRegisters.LockAsync();
      ir.Data[2] = 0x55;
      ir.Data[3] = 0xAA;
    }

    // ---- FC01: ReadCoils ----
    var coilsResult = await _client.ReadCoilsAsync(1, 0, 4);
    Assert.Equal(1, coilsResult.Length); // ceil(4/8) = 1 byte
    Assert.Equal(0b_0000_1101, coilsResult.Span[0]); // bit0=1,bit1=0,bit2=1,bit3=1

    // ---- FC02: ReadDiscreteInputs ----
    var diResult = await _client.ReadDiscreteInputsAsync(1, 5, 3);
    Assert.Equal(1, diResult.Length);
    Assert.Equal(0b_0000_0101, diResult.Span[0]); // bit0(addr5)=1, bit1(addr6)=0, bit2(addr7)=1

    // ---- FC03: ReadHoldingRegisters ----
    var hrResult = await _client.ReadHoldingRegistersAsync(1, 0, 2);
    Assert.Equal(4, hrResult.Length); // 2 registers × 2 bytes
    Assert.Equal(0x12, hrResult.Span[0]);
    Assert.Equal(0x34, hrResult.Span[1]);
    Assert.Equal(0xAB, hrResult.Span[2]);
    Assert.Equal(0xCD, hrResult.Span[3]);

    // ---- FC04: ReadInputRegisters ----
    var irResult = await _client.ReadInputRegistersAsync(1, 1, 1);
    Assert.Equal(2, irResult.Length);
    Assert.Equal(0x55, irResult.Span[0]); // register addr=1 → 0x55AA
    Assert.Equal(0xAA, irResult.Span[1]);

    // ---- FC05: WriteSingleCoil ----
    await _client.WriteSingleCoilAsync(1, 10, true);
    var coil10 = await _client.ReadCoilsAsync(1, 10, 1);
    Assert.Equal(0x01, coil10.Span[0]);

    // ---- FC06: WriteSingleRegister ----
    await _client.WriteSingleRegisterAsync(1, 8, new byte[]
    {
      0xDE, 0xAD
    });
    var reg8 = await _client.ReadHoldingRegistersAsync(1, 8, 1);
    Assert.Equal(0xDE, reg8.Span[0]);
    Assert.Equal(0xAD, reg8.Span[1]);

    // ---- FC15: WriteMultipleCoils ----
    // 写入 5 个线圈: ON, OFF, ON, ON, OFF (地址 20~24)
    await _client.WriteMultipleCoilsAsync(1, 20, 5, new byte[]
    {
      0b_0000_1101
    });
    var coilsBatch = await _client.ReadCoilsAsync(1, 20, 5);
    Assert.Equal(0b_0000_1101, coilsBatch.Span[0]);

    // ---- FC16: WriteMultipleRegisters ----
    await _client.WriteMultipleRegistersAsync(1, 40,
      new byte[]
      {
        0x11, 0x22, 0x33, 0x44, 0x55, 0x66
      });
    var regsBatch = await _client.ReadHoldingRegistersAsync(1, 40, 3);
    Assert.Equal(6, regsBatch.Length);
    Assert.Equal(0x11, regsBatch.Span[0]);
    Assert.Equal(0x22, regsBatch.Span[1]);
    Assert.Equal(0x33, regsBatch.Span[2]);
    Assert.Equal(0x44, regsBatch.Span[3]);
    Assert.Equal(0x55, regsBatch.Span[4]);
    Assert.Equal(0x66, regsBatch.Span[5]);
  }

  /// <summary>
  ///   连续发送大量相同类型的请求，测试服务端反复解码能力和客户端反复解码能力。
  /// </summary>
  [Fact]
  public async Task SendRepeatedSameRequests_ServerAndClientDecodeConsistently()
  {
    const int iterations = 50;

    // 初始化一段寄存器数据
    {
      using var hr = await _server.HoldingRegisters.LockAsync();
      // Data 是按字节索引的：每个寄存器占 2 字节
      for (var i = 0; i < 20; i++)
        hr.Data[i] = (byte)i;
    }

    // 反复读相同地址
    for (var round = 0; round < iterations; round++)
    {
      var result = await _client.ReadHoldingRegistersAsync(1, 0, 5);
      Assert.Equal(10, result.Length);
      Assert.Equal(0x00, result.Span[0]);
      Assert.Equal(0x01, result.Span[1]);
      Assert.Equal(0x08, result.Span[8]);
      Assert.Equal(0x09, result.Span[9]);
    }

    // 反复写-读不同地址，验证每次都能正确解码
    for (var round = 0; round < iterations; round++)
    {
      var addr = round % 50 + 10;
      var val = (byte)(round & 0xFF);
      await _client.WriteSingleRegisterAsync(1, addr, new[]
      {
        val, (byte)~val
      });
      var readBack = await _client.ReadHoldingRegistersAsync(1, addr, 1);
      Assert.Equal(val, readBack.Span[0]);
      Assert.Equal((byte)~val, readBack.Span[1]);
    }
  }

  /// <summary>
  ///   交叉发送不同类型的请求（并发），测试服务端在混合请求下的解码正确性。
  /// </summary>
  [Fact]
  public async Task SendMixedRequestsConcurrently_ServerDecodesAllCorrectly()
  {
    // 准备初始状态
    {
      using var coils = await _server.Coils.LockAsync();
      coils.Data.Set(0, false);
    }
    {
      using var hr = await _server.HoldingRegisters.LockAsync();
      hr.Data[100] = 0xAA;
      hr.Data[101] = 0xBB;
    }

    // 并发：读线圈 + 写单线圈 + 读保持寄存器 + 写单寄存器
    var t1 = _client.ReadCoilsAsync(1, 0, 1).AsTask();
    var t2 = _client.WriteSingleCoilAsync(1, 0, true).AsTask();
    var t3 = _client.ReadHoldingRegistersAsync(1, 100, 1).AsTask();
    var t4 = _client.WriteSingleRegisterAsync(1, 200, new byte[]
    {
      0x77, 0x88
    }).AsTask();

    await Task.WhenAll(t1, t2, t3, t4);

    // 验证读线圈结果（并发时可能在写之前或之后读到，只要不是错误即可）
    var coilResult = await t1;
    var coilBit = coilResult.Span[0];
    Assert.True(coilBit is 0x00 or 0x01);

    // 验证读寄存器结果（写入前读到旧值，写入后读到新值，不应受并发写影响）
    var regVal = await t3;
    Assert.Equal(2, regVal.Length);

    // 回读确认并发写入最终生效
    var coilFinal = await _client.ReadCoilsAsync(1, 0, 1);
    Assert.Equal(0x01, coilFinal.Span[0]);

    var regFinal = await _client.ReadHoldingRegistersAsync(1, 200, 1);
    Assert.Equal(0x77, regFinal.Span[0]);
    Assert.Equal(0x88, regFinal.Span[1]);
  }

  /// <summary>
  ///   请求非法数据地址时（地址+数量超出 65536），服务端返回 IllegalDataAddress (0x02)，
  ///   客户端应正确解码异常响应并抛出包含正确 ExceptionCode 的 <see cref="SbModbusException" />。
  /// </summary>
  [Fact]
  public async Task ReadCoils_OutOfRange_ThrowsIllegalDataAddress()
  {
    // 请求地址 + 数量超出 65536 范围，触发服务端 IsAddressOutOfRange
    var ex = await Assert.ThrowsAsync<SbModbusException>(() =>
      _client.ReadCoilsAsync(1, 65500, 100).AsTask());
    Assert.Equal(ModbusExceptionCode.IllegalDataAddress, ex.ExceptionCode);
  }

  /// <summary>
  ///   通过客户端 API 请求无效数据时（如写线圈数据非 0xFF00/0x0000），
  ///   客户端本地校验直接抛出 IllegalDataValue (0x03)，不会发到服务端。
  /// </summary>
  [Fact]
  public async Task WriteSingleCoil_InvalidData_ThrowsLocally()
  {
    var ex = await Assert.ThrowsAsync<SbModbusException>(() =>
      _client.WriteSingleCoilAsync(1, 0, new byte[]
      {
        0xAA, 0xBB
      }).AsTask());
    // 客户端 ValidateSingleCoilData 在发送前捕获，不经过网络
    Assert.Contains("0xFF00", ex.Message);
  }
}
