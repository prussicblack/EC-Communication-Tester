using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SOEM_FrontEnd.ViewModels
{
    public partial class MotorControlViewModel : ViewModelBase
    {

        private string _AxisName;

        public string AxisName
        {
            get
            {
                return _AxisName;
            }
            set
            {
                _AxisName = value;
                OnPropertyChanged(nameof(AxisName));
            }
        }
        public bool IsServoOn { get; set; }
        public bool HasError { get; set; }
        public bool IsHomed { get; set; }
        public bool IsInPosition { get; set; }

        public double CurrentPosition { get; set; }
        public double CurrentVelocity { get; set; }

        public string SpeedText { get; set; }
        public string AccelTimeText { get; set; }
        public string DecelTimeText { get; set; }
        public string IncDistanceText { get; set; }
        public string AbsPositionText { get; set; }
        public string StartPositionText { get; set; }
        public string EndPositionText { get; set; }

        public ICommand CmdMoveIncMinus { get; set; }
        public ICommand CmdMoveIncPlus { get; set; }
        public ICommand CmdServoOn { get; set; }
        public ICommand CmdServoOff { get; set; }
        public ICommand CmdAlarmClear { get; set; }
        public ICommand CmdHome { get; set; }
        public ICommand CmdMoveAbs { get; set; }
        public ICommand CmdMoveToStart { get; set; }
        public ICommand CmdMoveToEnd { get; set; }
        public ICommand CmdStartRepeat { get; set; }
        public ICommand CmdStopRepeat { get; set; }

        public MotorControlViewModel()
        {

        }




    }
}
