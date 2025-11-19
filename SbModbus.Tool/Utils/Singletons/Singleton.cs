using System;
using Sb.Extensions.System.Threading;

// ReSharper disable UnusedMember.Global

namespace SbModbus.Tool.Utils.Singletons;

public class Singleton<T> : ISingleton<T> where T : class, new()
{
  // ReSharper disable once InconsistentNaming
  private static readonly Lazy<T> instance = new(() => new T());

  protected static AsyncLock Lock { get; } = new();

  public static T Instance => instance.Value;
}