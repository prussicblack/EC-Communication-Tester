using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SOEM_FrontEnd.ViewModels
{
    public sealed class LoadingViewModel : INotifyPropertyChanged
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }
}
