using NetCoreServer;

namespace SbModbus.TcpStream;

/// <summary>
/// </summary>
public class SbTcpServerStream
{
}

/// <summary>
/// </summary>
public class SbTcpSession : TcpSession
{
  /// <summary>
  /// </summary>
  /// <param name="server"></param>
  public SbTcpSession(TcpServer server) : base(server)
  {
  }
}