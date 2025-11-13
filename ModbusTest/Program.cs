// See https://aka.ms/new-console-template for more information

using System.Runtime.CompilerServices;
using ModbusTest;
using SbModbus.Services.ModbusClient;
using SbModbus.TcpStream;

var s = new SbTcpClientStream("192.168.20.200", 502)
{
  ReadTimeout = 1000,
  WriteTimeout = 1000
};
// s.ConnectAsync();

var modbusClient = new ModbusRtuClient(s);

while (true)
{
  var bs1 = await modbusClient.ReadHoldingRegistersAsync(1, 1, (ushort)(Unsafe.SizeOf<MyStruct>() / 2));
  var setting = new MyStruct(bs1.Span);

  Console.WriteLine(
    $@"{setting.OutputVoltage:F3}, {setting.OutputCurrent:F3}, {setting.OutputPower:F3}, {DateTime.Now.Millisecond}");
}