using System.Net;
using System.Net.Sockets;
using SbModbus;
using SbModbus.Models;
using SbModbus.Services.ModbusClient;
using SbModbus.Services.ModbusServer;
using SbModbus.TcpStream;

namespace SbModbus.Tests;

public class ModbusTcpE2ETests : IAsyncLifetime
{
    private SbTcpServerStream _serverStream = null!;
    private ModbusTcpServer _server = null!;
    private SbTcpClientStream _clientStream = null!;
    private ModbusTcpClient _client = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        var probeStream = new SbTcpServerStream(IPAddress.Loopback, 0);
        probeStream.Start();
        _port = probeStream.LocalEndPoint!.Port;
        probeStream.Stop();
        probeStream.Dispose();
        await Task.Delay(100);

        _serverStream = new SbTcpServerStream(IPAddress.Loopback, _port);
        _server = new ModbusTcpServer();
        _server.UnitIdentifier.Add(1);
        _ = _server.StartAsync(_serverStream);
        await Task.Delay(300);

        _clientStream = new SbTcpClientStream(IPAddress.Loopback, _port);
        _client = new ModbusTcpClient(_clientStream);
        await _clientStream.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        // 先停止服务端，再断开客户端
        try { await _server.StopAsync(); _serverStream.Stop(); _serverStream.Dispose(); _server.Dispose(); } catch { }
        try { await _clientStream.DisconnectAsync(); _clientStream.Dispose(); _client.Dispose(); } catch { }
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

        await _client.WriteMultipleRegistersAsync(1, 22, new byte[] { 0x11, 0x11, 0x22, 0x22 });
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
        var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
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
        var t2 = _client.WriteSingleRegisterAsync(1, 55, new byte[] { 0x12, 0x34 }).AsTask();

        await Task.WhenAll(t1, t2);

        Assert.True(await fired1.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await fired2.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
