using System;
using Avalonia.Controls;
using SbModbus.Tool.Utils;

namespace SbModbus.Tool.Views.Windows;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    InitializeComponent();
    this.AutoCreateViewModel();
  }
}