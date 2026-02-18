using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SOEM_FrontEnd.ViewModels;

namespace SOEM_FrontEnd.Views;

public partial class MotorControl : UserControl
{
    public MotorControl()
    {
        InitializeComponent();
        //axaml에서 이어줄거임.
        //this.DataContext = new MotorControlViewModel();
    }
}