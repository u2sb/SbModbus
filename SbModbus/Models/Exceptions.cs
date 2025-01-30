using System;

namespace SbModbus.Models;

public class InvalidArrayLengthException(int expectedLength, int actualLength)
  : Exception($"Invalid array length. Expected: {expectedLength}, Actual: {actualLength}")
{
  public int ExpectedLength { get; } = expectedLength;
  public int ActualLength { get; } = actualLength;
}

// https://raw.githubusercontent.com/Apollo3zehn/FluentModbus/f2d74c6e45ff4b3b41d33ae7d7b3e6243b128472/src/FluentModbus/ModbusException.cs
public class ModbusException : Exception
{
  public ModbusException(string message) : base(message)
  {
    ExceptionCode = (ModbusExceptionCode)255;
  }

  public ModbusException(ModbusExceptionCode exceptionCode, string message) : base(message)
  {
    ExceptionCode = exceptionCode;
  }

  /// <summary>
  ///   The Modbus exception code. A value of 255 indicates that there is no specific exception code.
  /// </summary>
  public ModbusExceptionCode ExceptionCode { get; }
}