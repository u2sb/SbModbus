namespace SbModbus.Tool.Services.DataTransferServices;

public enum DataTransferType
{
  None = 0,
  SerialPort = 1,
  TcpClient = 2,
  TcpServer = 3,
  UdpClient = 4,
  UdpServer = 5
}