using Avalonia.Controls;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.Views.Pages;

public partial class ModbusClientPage : UserControl
{
  public ModbusClientPage()
  {
    InitializeComponent();
    this.AutoCreateViewModel();
  }
}
