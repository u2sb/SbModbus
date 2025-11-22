using System.Windows.Controls;
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