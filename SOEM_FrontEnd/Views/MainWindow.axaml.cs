using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using System;

namespace SOEM_FrontEnd.Views;

public partial class MainWindow : Window
{
    private AboutWindow _about;

    public static readonly StyledProperty<Thickness> WindowPaddingProperty =
       AvaloniaProperty.Register<MainWindow, Thickness>(nameof(WindowPadding));

    //종료시 shutdown호출을 위한 코드.
    private bool _disposedViewModel;
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_disposedViewModel)
        {
            _disposedViewModel = true;

            IDisposable disposable = DataContext as IDisposable;

            if (disposable != null)
            {
                disposable.Dispose();
            }
        }

        base.OnClosing(e);
    }

    public Thickness WindowPadding
    {
        get { return GetValue(WindowPaddingProperty); }
        set { SetValue(WindowPaddingProperty, value); }
    }

    public MainWindow()
    {
        InitializeComponent();
        UpdatePadding();
        this.PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
                UpdatePadding();
        };
    }

    private void About_Click(object sender, RoutedEventArgs e)
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
    private void TitleBar_PointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
    private void TitleBar_DoubleTapped(object sender, TappedEventArgs e)
    {
        // 리사이즈 불가면(지금 CanResize=False) 토글 의미 없음
        // MainWindow에서 토글 쓰려면 CanResize=True 여야 함

        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }
    private void UpdatePadding()
    {
        // 최대화일 때 보정
        if (WindowState == WindowState.Maximized)
            WindowPadding = new Thickness(5);
        else
            WindowPadding = new Thickness(0);
    }

}
