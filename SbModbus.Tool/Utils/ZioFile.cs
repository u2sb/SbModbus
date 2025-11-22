using SbModbus.Tool.Utils.Singletons;
using Zio;
using Zio.FileSystems;

namespace SbModbus.Tool.Utils;

public class ZioFile : Singleton<ZioFile>
{
  public UPath RootPath { get; } = Environment.CurrentDirectory;

  public IFileSystem FileSystem { get; } = new PhysicalFileSystem();

  /// <summary>
  ///   获取路径
  /// </summary>
  /// <param name="relativePath"></param>
  /// <returns></returns>
  public UPath GetPath(string relativePath)
  {
    return UPath.Combine(RootPath, relativePath);
  }
}