using Sb.Extensions.System.Threading;

// ReSharper disable UnusedMember.Global

namespace SbModbus.Tool.Utils.Singletons;

public interface ISingleton<T> where T : class
{
  public static T Instance { get; } = null!;

  protected static AsyncLock Lock { get; } = new();
}