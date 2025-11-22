using System.Buffers;
using System.IO.Ports;

namespace SbModbus.Tool.Services.DataTransferServices;

public class SerialPortDtStream : IDtStream
{
  public SerialPortDtStream()
  {
    SerialPort = new SerialPort();
    SerialPort.DataReceived += SerialPortOnDataReceived;
    SerialPort.ErrorReceived += SerialPortOnErrorReceived;
    SerialPort.PinChanged += SerialPortOnPinChanged;
  }

  public SerialPort SerialPort { get; }

  public void Dispose()
  {
    SerialPort.Dispose();
    GC.SuppressFinalize(this);
  }

  public ReadOnlySpanAction<byte, IDtStream>? OnDataWrite { get; set; }
  public ReadOnlySpanAction<byte, IDtStream>? OnDataReceived { get; set; }
  public Action<bool>? OnConnectStateChanged { get; set; }

  public bool IsConnected => SerialPort.IsOpen;

  public bool Connect()
  {
    Disconnect();

    SerialPort.Open();
    OnConnectStateChanged?.Invoke(IsConnected);
    return IsConnected;
  }

  public void Disconnect()
  {
    if (IsConnected)
    {
      SerialPort.Close();
      OnConnectStateChanged?.Invoke(IsConnected);
    }
  }

  public void Write(ReadOnlySpan<byte> data)
  {
    if (!IsConnected)
    {
      return;
    }

    SerialPort.BaseStream.Write(data);
    SerialPort.BaseStream.Flush();
    OnDataWrite?.Invoke(data, this);
  }

  public async ValueTask WriteAsync(ReadOnlyMemory<byte> data)
  {
    if (!IsConnected)
    {
      return;
    }

    await SerialPort.BaseStream.WriteAsync(data);
    await SerialPort.BaseStream.FlushAsync();
    OnDataWrite?.Invoke(data.Span, this);
  }


  #region 事件

  private void SerialPortOnPinChanged(object sender, SerialPinChangedEventArgs e)
  {
    // 暂时不处理引脚变化
  }

  private void SerialPortOnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
  {
    // 暂时不处理错误
  }

  private void SerialPortOnDataReceived(object sender, SerialDataReceivedEventArgs e)
  {
    if (sender is not SerialPort sp)
    {
      return;
    }

    var len = sp.BytesToRead;
    Span<byte> buf = stackalloc byte[len];
    sp.BaseStream.ReadExactly(buf);
    OnDataReceived?.Invoke(buf, this);
  }

  #endregion
}