using Avalonia.Controls;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.Views.Pages;

public partial class ModbusServerPage : UserControl
{
  public ModbusServerPage()
  {
    InitializeComponent();
    this.AutoCreateViewModel();
  }
}
