// See https://aka.ms/new-console-template for more information

using ModbusTest;
using RJCP.IO.Ports;
using SbModbus.Client;
using SbModbus.Models;
using SbModbus.Utils;

Console.WriteLine("Hello, World!");

var sp = new SerialPortStream("COM34")
{
  BaudRate = 9600,
  ReadTimeout = 1000
};

sp.Open();

IModbusClient modbusClient = new ModbusRtuClient(sp);


await modbusClient.WriteMultipleRegistersAsync(1, 4,
  new Memory<byte>([
    ..65538.ToBytes(BigAndSmallEndianEncodingMode.ABCD), ..((ushort)6550).ToBytes(BigAndSmallEndianEncodingMode.ABCD)
  ]));

var b = await modbusClient.ReadHoldingRegistersAsync(1, 4, 3);
//
// Console.WriteLine(BitConverter.ToInt32(b.Span.AsBytes(), true));

var myStruct = new MyStruct(b.Span);

Console.WriteLine(myStruct.B);


sp.Close();
sp.Dispose();