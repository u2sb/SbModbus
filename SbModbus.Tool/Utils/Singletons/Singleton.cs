using Sb.Extensions.System.Threading;

// ReSharper disable UnusedMember.Global

namespace SbModbus.Tool.Utils.Singletons;

public class Singleton<T> : ISingleton<T> where T : class, ISingleton<T>
{
  // ReSharper disable once InconsistentNaming
  private static readonly Lazy<T> instance = new(() =>
    Activator.CreateInstance(typeof(T), true) as T ?? throw new InvalidOperationException());

  protected AsyncLock AsyncLock { get; } = new();

  protected Lock Lock { get; } = new();

  public static T Instance => instance.Value;
}
