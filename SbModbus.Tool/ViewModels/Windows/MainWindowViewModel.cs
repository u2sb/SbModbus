using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pipboy.Avalonia;
using R3;
using SbModbus.Tool.Services.RecordServices;

namespace SbModbus.Tool.ViewModels.Windows;

[AutoInject]
public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
  private DisposableBag _disposableBag;

  public Dictionary<string, string> ThemeColors { get; } = new()
  {
    { "PIPBOY GREEN", "#15FF52" },
    { "AMBER", "#FFA500" },
    { "BLUE", "#00BFFF" },
    { "RED", "#FF3030" },
    { "CYAN", "#00FFEE" },
    { "GOLD", "#FFD700" },
    { "PURPLE", "#BB44FF" },
    { "ORANGE", "#FF8C00" }
  };

  [ObservableProperty] public partial string SelectedTheme { get; set; } = "#15FF52";

  public void Dispose()
  {
    _disposableBag.Dispose();
    GC.SuppressFinalize(this);
  }

  partial void OnSelectedThemeChanged(string value)
  {
    PipboyThemeManager.Instance.TrySetPrimaryColor(value);
  }

  [RelayCommand]
  private void OpenSbModbus()
  {
    Process.Start(new ProcessStartInfo
    {
      FileName = "https://github.com/u2sb/SbModbus",
      UseShellExecute = true
    });
  }

  [RelayCommand]
  private void OpenLogDirectory()
  {
    if (Uri.TryCreate(DataTransmissionRecord.LogDirPath, UriKind.Absolute, out var uri))
    {
      var dir = uri.LocalPath;
      if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

      // 根据操作系统选择打开方式
      if (OperatingSystem.IsWindows())
        // 在Windows中，使用explorer打开文件夹
        Process.Start("explorer.exe", dir);
      else if (OperatingSystem.IsMacOS())
        // 在macOS中，使用open命令打开文件夹
        Process.Start("open", dir);
      else if (OperatingSystem.IsLinux())
        // 在Linux中，使用xdg-open打开文件夹（适用于大多数桌面环境）
        Process.Start("xdg-open", dir);
      else
        throw new PlatformNotSupportedException("不支持的操作系统");
    }
  }
}
