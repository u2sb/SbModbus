using System;

namespace SbModbus.Models;

/// <summary>
///   Modbus请求的返回
/// </summary>
public class ModbusResponse
{
  /// <summary>
  ///   响应状态
  /// </summary>
  public enum ResponseStatus
  {
    /// <summary>
    ///   准备
    /// </summary>
    Ready = 0,

    /// <summary>
    ///   请求中
    /// </summary>
    Requesting = 1,

    /// <summary>
    ///   成功
    /// </summary>
    Success = 2,

    /// <summary>
    ///   超时
    /// </summary>
    Timeout = 3,

    /// <summary>
    ///   失败（包括通信异常和Modbus异常）
    /// </summary>
    Exception = 4
  }

  /// <summary>
  ///   当前状态
  /// </summary>
  public ResponseStatus Status { get; set; } = ResponseStatus.Ready;

  /// <summary>
  ///   结果数据
  /// </summary>
  public byte[] Data { get; set; } = [];

  /// <summary>
  ///   附加信息（如超时原因、异常说明）
  /// </summary>
  public string? Message { get; set; }

  /// <summary>
  ///   Modbus异常码（异常响应时可用）
  /// </summary>
  public ModbusExceptionCode? ExceptionCode { get; set; }

  /// <summary>
  ///   异常对象（本地调用异常）
  /// </summary>
  public Exception? Error { get; set; }
}
