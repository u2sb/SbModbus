using System.Reflection;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace SbModbus.Tool.ViewModels;

public static class ServiceCollectionExtensions
{
  extension(IServiceCollection services)
  {
    /// <summary>
    ///   自动注入View和ViewModel
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public IServiceCollection AddViewsAndViewModels(Type type)
    {
      return services.AddViewsAndViewModels(type.Assembly);
    }

    /// <summary>
    ///   自动注入View和ViewModel
    /// </summary>
    /// <param name="assembly"></param>
    /// <returns></returns>
    public IServiceCollection AddViewsAndViewModels(Assembly assembly)
    {
      foreach (var type in assembly.GetTypes())
        if (type is { IsClass: true, IsAbstract: false } &&
            (typeof(ViewModelBase).IsAssignableFrom(type) || typeof(Control).IsAssignableFrom(type)))
        {
          var attribute = type.GetCustomAttribute<AutoInjectAttribute>();
          if (attribute is not null)
            switch (attribute.Lifetime)
            {
              case ServiceLifetime.Singleton:
                services.AddSingleton(type);
                break;
              case ServiceLifetime.Scoped:
                services.AddScoped(type);
                break;
              case ServiceLifetime.Transient:
                services.AddTransient(type);
                break;
              default:
                services.AddSingleton(type);
                break;
            }
        }

      return services;
    }
  }
}

[AttributeUsage(AttributeTargets.Class)]
public class AutoInjectAttribute(ServiceLifetime lifetime = ServiceLifetime.Singleton) : Attribute
{
  public ServiceLifetime Lifetime { get; } = lifetime;
}
