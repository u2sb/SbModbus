using System;
using System.Threading;
using System.Threading.Tasks;
using SbModbus.Services.ModbusClient;

namespace SbModbus.Models;

/// <remarks>
///   此文件中的委托被 <see cref="SbModbus.Services.ModbusClient.IModbusClient"/> 的事件
///   和 <see cref="SbModbus.Models.IModbusStream"/> 的 OnConnectStateChanged 事件共同使用。
///   <see cref="ModbusStreamStateHandler{T}"/> 的 sender 参数固定为 IModbusClient，
///   在用于 IModbusStream 场景时调用者负责传递正确的发送方。
/// </remarks>

/// <summary>
///   表示当通信通道传输数据时触发的回调方法
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的只读数据内存块</param>
public delegate void ModbusClientHandler(IModbusClient sender, ReadOnlyMemory<byte> data);

/// <summary>
///   表示当通信通道传输数据时触发的异步回调方法
/// </summary>
/// <param name="sender">触发事件的传输对象</param>
/// <param name="data">接收到的只读数据内存块</param>
/// <param name="ct"></param>
/// <returns>表示异步操作的任务</returns>
public delegate Task ModbusClientAsyncHandler(IModbusClient sender, ReadOnlyMemory<byte> data, CancellationToken ct);

/// <summary>
///   状态改变时触发的回调方法
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="sender"></param>
/// <param name="state"></param>
public delegate void ModbusStreamStateHandler<in T>(IModbusClient sender, T state);
