using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using SbModbus.Tool.Utils;
using SbModbus.Tool.ViewModels;

namespace SbModbus.Tool.Views.Windows;

[AutoInject(ServiceLifetime.Transient)]
public partial class MainWindow : Window
{
  public MainWindow()
  {
    FontFamily = this.FindResource("SarasaFonts") as FontFamily ?? FontFamily;
    InitializeComponent();
    this.AutoCreateViewModel();
  }
}
