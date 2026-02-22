using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using SOEM_FrontEnd.Ethercat.ESI;
using SOEM_FrontEnd.Util.Logging;
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

        //로깅시작 초기화
        OPLogger.Configure(new OPLoggerOptions
        {
            LogFolder = "OPLogs",
            FileNamePrefix = "soem_",
            MaxFolderBytes = 512L * 1024L * 1024L,   // 512MB
            //MaxFolderBytes = 128L * 1024L,   // 128KB
            MaxActiveFileBytes = 32L * 1024L * 1024L, // 32MB
            //MaxActiveFileBytes = 32L * 1024L, // 32KB
            QueueCapacity = 8192,
            QueueMode = OPLogQueueMode.Block,
            MinimumLevel = LogLevel.Information,
            EnableFailover = true
        });

        var log = OPLogger.CreateLogger("App");
        log.LogInformation("App boot");

        //비선언 타입 초기화.
        //Log.SetDefaultCategory("SOEM");
        //로깅시작 초기화 완료.


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
            splash.Show();

            // “첫 렌더 이후” 실행되게 UI 큐에 던짐 (핵심)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = RunStartupAsync(desktop, splash, loadingVm);
            }, Avalonia.Threading.DispatcherPriority.Background);

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
                string path = AppDomain.CurrentDomain.BaseDirectory + "ESI";
                ESICatalog.Initialize(path);

                Report(vm, 0.30, "Initializing map...");
                // EthercatMapStore.Instance.Init(...)

                Report(vm, 0.60, "Building UI models...");
                // SDO RowVm 목록 생성(정의 기반)

                Report(vm, 0.85, "Connecting to device...");
                // (선택) 초기 스캔/연결

                Report(vm, 1.00, "Done.");

                throw new Exception("Test");
            });

            //await Dispatcher.UIThread.InvokeAsync(() =>
            //{
            var main = new MainWindow
            {
                DataContext = new MainViewModel()
            };

            desktop.MainWindow = main;
            main.Show();
            splash.Close();
            //});
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.IsIndeterminate = false;
                vm.Closeable = true;
                vm.StatusText = "Startup failed: " + ex.ToString();

                //로그기록
                var log = OPLogger.CreateLogger("App");
                log.LogInformation($"\"Startup failed: \" + {ex.ToString()}");
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
