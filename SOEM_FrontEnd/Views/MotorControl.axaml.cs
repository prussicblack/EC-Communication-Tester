using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SOEM_FrontEnd.ViewModels;

namespace SOEM_FrontEnd.Views;

public partial class MotorControl : UserControl
{
    private Button _jogMinusButton;
    private Button _jogPlusButton;
    
    public MotorControl()
    {
        InitializeComponent();
        //상위에서 이어줄거임.
        //this.DataContext = new MotorControlViewModel();

        _jogMinusButton = this.FindControl<Button>("JogMinusButton");
        _jogPlusButton = this.FindControl<Button>("JogPlusButton");

        _jogMinusButton.AddHandler(
            InputElement.PointerPressedEvent,
            JogMinus_PointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _jogMinusButton.AddHandler(
            InputElement.PointerReleasedEvent,
            JogButton_PointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _jogMinusButton.AddHandler(
            InputElement.PointerCaptureLostEvent,
            JogButton_PointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _jogPlusButton.AddHandler(
            InputElement.PointerPressedEvent,
            JogPlus_PointerPressed,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _jogPlusButton.AddHandler(
            InputElement.PointerReleasedEvent,
            JogButton_PointerReleased,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);

        _jogPlusButton.AddHandler(
            InputElement.PointerCaptureLostEvent,
            JogButton_PointerCaptureLost,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void JogMinus_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Button button = sender as Button;
        if (button != null)
            e.Pointer.Capture(button);

        MotorControlViewModel vm = DataContext as MotorControlViewModel;
        if (vm != null)
            vm.JogMinusPressed();

        e.Handled = true;
    }

    private void JogPlus_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Button button = sender as Button;
        if (button != null)
            e.Pointer.Capture(button);

        MotorControlViewModel vm = DataContext as MotorControlViewModel;
        if (vm != null)
            vm.JogPlusPressed();

        e.Handled = true;
    }

    private void JogButton_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);

        MotorControlViewModel vm = DataContext as MotorControlViewModel;
        if (vm != null)
            vm.JogReleased();

        e.Handled = true;
    }

    private void JogButton_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        MotorControlViewModel vm = DataContext as MotorControlViewModel;
        if (vm != null)
            vm.JogReleased();
    }


}