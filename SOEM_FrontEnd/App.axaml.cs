using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SOEM_FrontEnd.ViewModels;
using SOEM_FrontEnd.Views;
using System;
using System.Threading.Tasks;

namespace SOEM_FrontEnd;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        //BindingPlugins.DataValidators.RemoveAt(0);

        //if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        //{
        //    desktop.MainWindow = new MainWindow
        //    {
        //        DataContext = new MainViewModel()
        //    };
        //}
        //else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        //{
        //    singleViewPlatform.MainView = new MainView
        //    {
        //        DataContext = new MainViewModel()
        //    };
        //}

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loadingVm = new LoadingViewModel
            {
                StatusText = "Starting...",
                IsIndeterminate = false,
                Progress = 0
            };

            var splash = new SplashWindow
            {
                DataContext = loadingVm
            };

            //splash.Show();
            desktop.MainWindow = splash; // 포커스/종료 처리 안정화를 위해

            splash.Opened += async (_, __) =>
            {
                // “한 번 그려질 기회”를 강제로 줌
                await Avalonia.Threading.Dispatcher.UIThread
                    .InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);

                _ = RunStartupAsync(desktop, splash, loadingVm);
            };

            splash.Show();

            //_ = RunStartupAsync(desktop, splash, loadingVm);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task RunStartupAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash, LoadingViewModel vm)
    {
        try
        {
            await Task.Run(() =>
            {
                // 초기화 단계들
                Report(vm, 0.05, "Loading ESI...");
                // ESI 로드/파싱

                Report(vm, 0.30, "Initializing map...");
                // EthercatMapStore.Instance.Init(...)

                Report(vm, 0.60, "Building UI models...");
                // SDO RowVm 목록 생성(정의 기반)

                Report(vm, 0.85, "Connecting to device...");
                // (선택) 초기 스캔/연결

                Report(vm, 1.00, "Done.");
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var main = new MainWindow
                {
                    DataContext = new MainViewModel()
                };

                desktop.MainWindow = main;
                main.Show();
                splash.Close();
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.IsIndeterminate = false;
                vm.StatusText = "Startup failed: " + ex.Message;
                // 필요하면 로그 + 재시도 버튼 등을 추가
            });
        }
    }

    private void Report(LoadingViewModel vm, double progress, string status)
    {
        // 백그라운드 스레드에서 UI 바인딩 변경하면 안 되므로 UIThread로 marshal
        Dispatcher.UIThread.Post(() =>
        {
            vm.Progress = progress;
            vm.StatusText = status;
        });
    }
}
