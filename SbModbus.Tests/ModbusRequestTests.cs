using System.Collections;
using SbModbus.Models;

namespace SbModbus.Tests;

public class ModbusRequestTests
{
  [Fact]
  public void CreateReadCoils_ValidatesCorrectly()
  {
    var req = ModbusRequest.CreateReadCoils(0, 8);
    Assert.Equal(ModbusFunctionCode.ReadCoils, req.FunctionCode);
    Assert.Equal((ushort)0, req.StartingAddress);
    Assert.Equal((ushort)8, req.Quantity);
    Assert.Equal((byte)1, req.SlaveId);
    Assert.Empty(req.Data);
    Assert.Equal(ModbusResponse.ResponseStatus.Ready, req.Response.Status);
  }

  [Fact]
  public void CreateReadDiscreteInputs_ValidatesCorrectly()
  {
    var req = ModbusRequest.CreateReadDiscreteInputs(10, 16, 3);
    Assert.Equal(ModbusFunctionCode.ReadDiscreteInputs, req.FunctionCode);
    Assert.Equal((ushort)10, req.StartingAddress);
    Assert.Equal((ushort)16, req.Quantity);
    Assert.Equal((byte)3, req.SlaveId);
  }

  [Fact]
  public void CreateReadHoldingRegisters_ValidatesCorrectly()
  {
    var req = ModbusRequest.CreateReadHoldingRegisters(100, 5);
    Assert.Equal(ModbusFunctionCode.ReadHoldingRegisters, req.FunctionCode);
    Assert.Equal((ushort)100, req.StartingAddress);
    Assert.Equal((ushort)5, req.Quantity);
  }

  [Fact]
  public void CreateReadInputRegisters_ValidatesCorrectly()
  {
    var req = ModbusRequest.CreateReadInputRegisters(200, 3);
    Assert.Equal(ModbusFunctionCode.ReadInputRegisters, req.FunctionCode);
    Assert.Equal((ushort)200, req.StartingAddress);
    Assert.Equal((ushort)3, req.Quantity);
  }

  [Fact]
  public void CreateWriteSingleCoil_On_SetsCorrectData()
  {
    var req = ModbusRequest.CreateWriteSingleCoil(5, true);
    Assert.Equal(ModbusFunctionCode.WriteSingleCoil, req.FunctionCode);
    Assert.Equal((ushort)5, req.StartingAddress);
    Assert.Equal((ushort)1, req.Quantity);
    Assert.Equal(0xFF, req.Data[0]);
    Assert.Equal(0x00, req.Data[1]);
  }

  [Fact]
  public void CreateWriteSingleCoil_Off_SetsCorrectData()
  {
    var req = ModbusRequest.CreateWriteSingleCoil(5, false);
    Assert.Equal(ModbusFunctionCode.WriteSingleCoil, req.FunctionCode);
    Assert.Equal(0x00, req.Data[0]);
    Assert.Equal(0x00, req.Data[1]);
  }

  [Fact]
  public void CreateWriteSingleRegister_WithBytes_ValidatesCorrectly()
  {
    var data = new byte[]
    {
      0x12, 0x34
    };
    var req = ModbusRequest.CreateWriteSingleRegister(7, data);
    Assert.Equal(ModbusFunctionCode.WriteSingleRegister, req.FunctionCode);
    Assert.Equal((ushort)7, req.StartingAddress);
    Assert.Equal((ushort)1, req.Quantity);
    Assert.Equal(0x12, req.Data[0]);
    Assert.Equal(0x34, req.Data[1]);
  }

  [Fact]
  public void CreateWriteSingleRegister_WithUshort_ValidatesCorrectly()
  {
    var req = ModbusRequest.CreateWriteSingleRegister(7, 0x1234);
    Assert.Equal(ModbusFunctionCode.WriteSingleRegister, req.FunctionCode);
    Assert.Equal((ushort)7, req.StartingAddress);
    // 大端模式: MSB 在前
    Assert.Equal(0x12, req.Data[0]);
    Assert.Equal(0x34, req.Data[1]);
  }

  [Fact]
  public void CreateWriteSingleRegister_LittleEndian_ReversesBytes()
  {
    var req = ModbusRequest.CreateWriteSingleRegister(7, 0x1234, false);
    // 小端模式: LSB 在前
    Assert.Equal(0x34, req.Data[0]);
    Assert.Equal(0x12, req.Data[1]);
  }

  [Fact]
  public void CreateWriteMultipleRegisters_ValidatesCorrectly()
  {
    var data = new byte[]
    {
      0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF
    };
    var req = ModbusRequest.CreateWriteMultipleRegisters(20, data);
    Assert.Equal(ModbusFunctionCode.WriteMultipleRegisters, req.FunctionCode);
    Assert.Equal((ushort)20, req.StartingAddress);
    Assert.Equal((ushort)3, req.Quantity); // 6 bytes / 2 = 3 registers
    Assert.Equal(0xAA, req.Data[0]);
    Assert.Equal(0xBB, req.Data[1]);
    Assert.Equal(0xCC, req.Data[2]);
  }

  [Fact]
  public void CreateWriteMultipleCoils_ValidatesCorrectly()
  {
    var bits = new BitArray(new[]
    {
      true, false, true, true, false, true, false, false
    });
    var req = ModbusRequest.CreateWriteMultipleCoils(30, bits);
    Assert.Equal(ModbusFunctionCode.WriteMultipleCoils, req.FunctionCode);
    Assert.Equal((ushort)30, req.StartingAddress);
    Assert.Equal((ushort)bits.Length, req.Quantity);
    Assert.True((req.Data[0] & 0x01) != 0); // 第一个 bit 为 true
  }

  [Fact]
  public void CreateReadRequest_InvalidQuantity_Zero_Throws()
  {
    Assert.Throws<SbModbusException>(() =>
      ModbusRequest.CreateReadRequest(ModbusFunctionCode.ReadCoils, 0, 0));
  }

  [Fact]
  public void CreateReadRequest_OutOfRange_Throws()
  {
    // 地址 + 数量 > 65536
    Assert.Throws<SbModbusException>(() =>
      ModbusRequest.CreateReadRequest(ModbusFunctionCode.ReadCoils, 65500, 100));
  }

  [Fact]
  public void CreateWriteRequest_InvalidDataLength_Throws()
  {
    // 写单个线圈需要 2 字节数据
    Assert.Throws<SbModbusException>(() =>
      ModbusRequest.CreateWriteRequest(ModbusFunctionCode.WriteSingleCoil, 0, [0xFF]));
  }

  [Fact]
  public void Reset_ResetsResponseToReady()
  {
    var req = ModbusRequest.CreateReadCoils(0, 8);
    req.Response.Status = ModbusResponse.ResponseStatus.Success;
    req.Response.Data = new byte[]
    {
      0x01
    };

    req.Reset();

    Assert.Equal(ModbusResponse.ResponseStatus.Ready, req.Response.Status);
    Assert.Empty(req.Response.Data);
  }
}
