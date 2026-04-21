using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.Views.Pages;

public partial class ModbusPage : UserControl
{
  public ModbusPage()
  {
    InitializeComponent();
    this.AutoCreateViewModel();
  }
}
