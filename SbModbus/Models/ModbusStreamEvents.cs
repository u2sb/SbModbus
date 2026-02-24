using System;
using System.Threading;
using System.Threading.Tasks;

namespace SbModbus.Models;

/// <summary>
///   表示当通信通道传输数据时触发的回调方法
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的只读数据内存块</param>
public delegate void ModbusStreamHandler(IModbusStream sender, ReadOnlySpan<byte> data);

/// <summary>
///   表示当通信通道传输数据时触发的异步回调方法
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的只读数据内存块</param>
/// <param name="ct"></param>
/// <returns>表示异步操作的任务</returns>
public delegate Task ModbusStreamAsyncHandler(IModbusStream sender, ReadOnlyMemory<byte> data, CancellationToken ct);

/// <summary>
///   状态改变时触发的回调方法
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="sender"></param>
/// <param name="state"></param>
public delegate void ModbusStreamStateHandler<in T>(IModbusStream sender, T state);
