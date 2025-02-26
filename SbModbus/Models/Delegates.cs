namespace SbModbus.Models;

/// <summary>
///   当被写时
///   <param name="offset">偏移</param>
///   <param name="length">长度</param>
/// </summary>
public delegate void OnWritten(int offset, int length);

