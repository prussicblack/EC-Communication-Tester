using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SOEM_FrontEnd.Views;

public partial class MainWindow : Window
{
    private AboutWindow _about;

    public MainWindow()
    {
        InitializeComponent();
    }
    private void About_Click(object? sender, RoutedEventArgs e)
    {
        if (_about == null)
        {
            _about = new AboutWindow();
            _about.Closed += (_, __) => { _about = null; };
            _about.Show(this);
            return;
        }

        _about.Activate();
    }
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

}
