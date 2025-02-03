// See https://aka.ms/new-console-template for more information

using ModbusTest;
using SbModbus.Models;
using SbModbus.Utils;

// Console.WriteLine("Hello, World!");
//
// var sp = new SerialPortStream("COM34")
// {
//   BaudRate = 9600,
//   ReadTimeout = 1000
// };
//
// sp.Open();
//
// IModbusClient modbusClient = new ModbusRtuClient(sp);
//
//
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


// sp.Close();
// sp.Dispose();


var a = new MyStruct
{
  B = 1234567890
};

var b = a.ToBytes();
var c = a.ToBytes(BigAndSmallEndianEncodingMode.DCBA);
var d = a.ToBytes(2);
var e = a.ToBytes(3);

Console.ReadKey();