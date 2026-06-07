namespace SbModbus.Tool.Services.ModbusServices;

/// <summary>
///   Modbus 服务端协议类型
/// </summary>
public enum ModbusServerProtocol
{
  Rtu,
  Tcp
}

/// <summary>
///   Modbus 服务端传输方式
/// </summary>
public enum ModbusServerTransport
{
  SerialPort,
  Tcp,
  Udp,
  TcpClient,
  UdpClient
}
