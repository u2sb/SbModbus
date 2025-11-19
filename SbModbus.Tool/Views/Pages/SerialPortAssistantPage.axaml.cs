using Avalonia.Controls;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.Views.Pages;

public partial class SerialPortAssistantPage : UserControl
{
  public SerialPortAssistantPage()
  {
    InitializeComponent();
    this.AutoCreateViewModel();
  }
}