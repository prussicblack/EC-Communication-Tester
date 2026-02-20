using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SOEM_FrontEnd.ViewModels;

namespace SOEM_FrontEnd.Views;

public partial class MainView : UserControl
{
    private bool _logHooked;
    
    public MainView()
    {
        InitializeComponent();

    }
   
}
