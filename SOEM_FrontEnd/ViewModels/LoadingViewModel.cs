using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;

namespace SOEM_FrontEnd.ViewModels
{
    public sealed class LoadingViewModel : ViewModelBase
    {
        private string _statusText;
        public string StatusText
        {
            get { return _statusText; }
            set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(); }
        }

        private double _progress; // 0~1
        public double Progress
        {
            get { return _progress; }
            set { if (_progress == value) return; _progress = value; OnPropertyChanged(); }
        }

        private bool _isIndeterminate;
        public bool IsIndeterminate
        {
            get { return _isIndeterminate; }
            set { if (_isIndeterminate == value) return; _isIndeterminate = value; OnPropertyChanged(); }
        }

        private bool _Closeable = false;

        public bool Closeable
        {
            get
            {
                return _Closeable;
            }

            set
            {
                if (_Closeable == value) return;
                _Closeable = value;
                OnPropertyChanged();
            }
        }

        public ICommand Close { get; private set; }

        public LoadingViewModel()
        {
            Close = new RelayCommand(HandleClose);
        }

        private void HandleClose()
        {
            IApplicationLifetime lifetime = Application.Current.ApplicationLifetime;

            IClassicDesktopStyleApplicationLifetime desktop =
                lifetime as IClassicDesktopStyleApplicationLifetime;
            
            if (desktop != null)
            {
                desktop.Shutdown();
                return;
            }

        }

        //public event PropertyChangedEventHandler? PropertyChanged;
        //private void OnPropertyChanged([CallerMemberName] string name = null)
        //{
        //    var h = PropertyChanged;
        //    if (h != null) h(this, new PropertyChangedEventArgs(name));
        //}
    }
}
