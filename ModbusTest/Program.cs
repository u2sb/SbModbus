// See https://aka.ms/new-console-template for more information

using ModbusTest;
using RJCP.IO.Ports;
using SbModbus.Client;
using SbModbus.Models;
using SbModbus.Utils;

Console.WriteLine("Hello, World!");

var sp = new SerialPortStream("COM3")
{
  BaudRate = 115200,
  ReadTimeout = 1000
};

sp.Open();

var modbusClient = new ModbusRtuClient(sp)
{
  ClearReadBufferAsync = async (s, ct) =>
  {
    if (s is SerialPortStream serialPortStream)
    {
      var b = serialPortStream.BytesToRead;
      if (b > 0)
      {
        var temp = new byte[b];

        await serialPortStream.ReadExactlyAsync(temp.AsMemory(0, b), ct);
      }
    }
  }
};

// await modbusClient.WriteMultipleRegistersAsync(1, 4,
//   new Memory<byte>([
//     ..65538.ToBytes(BigAndSmallEndianEncodingMode.ABCD), ..((ushort)6550).ToBytes(BigAndSmallEndianEncodingMode.ABCD)
//   ]));
//
// var b = await modbusClient.ReadHoldingRegistersAsync(1, 4, 3);
//
// var myStruct = new MyStruct(b.Span);
//
// Console.WriteLine(myStruct.MyStruct2.B);


var a = await modbusClient.ReadInputRegistersAsync(1, 0x10, 2);

sp.Close();
sp.Dispose();


// var a = new MyStruct
// {
//   B = 1234567890
// };
//
// var b = a.ToBytes();
// var c = a.ToBytes((byte)BigAndSmallEndianEncodingMode.DCBA);
// var d = a.ToBytes(2);
// var e = a.ToBytes(3);