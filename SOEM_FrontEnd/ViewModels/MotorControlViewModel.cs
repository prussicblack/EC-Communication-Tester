using CommunityToolkit.Mvvm.Input;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System.Windows.Input;

namespace SOEM_FrontEnd.ViewModels;

public partial class MotorControlViewModel : ViewModelBase
{
    private IMotorCommands _motor; // backend profile (optional)

    private string _axisName = "Axis";
    public string AxisName
    {
        get => _axisName;
        set => SetProperty(ref _axisName, value);
    }

    private bool _isServoOn;
    public bool IsServoOn
    {
        get => _isServoOn;
        set => SetProperty(ref _isServoOn, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    private bool _isHomed;
    public bool IsHomed
    {
        get => _isHomed;
        set => SetProperty(ref _isHomed, value);
    }

    private bool _isInPosition;
    public bool IsInPosition
    {
        get => _isInPosition;
        set => SetProperty(ref _isInPosition, value);
    }

    private int _currentPosition;
    public int CurrentPosition
    {
        get => _currentPosition;
        set => SetProperty(ref _currentPosition, value);
    }

    private int _currentVelocity;
    public int CurrentVelocity
    {
        get => _currentVelocity;
        set => SetProperty(ref _currentVelocity, value);
    }

    private string _speedText = "1000";
    public string SpeedText
    {
        get => _speedText;
        set => SetProperty(ref _speedText, value);
    }

    private string _accelTimeText = "100";
    public string AccelTimeText
    {
        get => _accelTimeText;
        set => SetProperty(ref _accelTimeText, value);
    }

    private string _decelTimeText = "100";
    public string DecelTimeText
    {
        get => _decelTimeText;
        set => SetProperty(ref _decelTimeText, value);
    }

    private string _incDistanceText = "100";
    public string IncDistanceText
    {
        get => _incDistanceText;
        set => SetProperty(ref _incDistanceText, value);
    }

    private string _absPositionText = "0";
    public string AbsPositionText
    {
        get => _absPositionText;
        set => SetProperty(ref _absPositionText, value);
    }

    private string _startPositionText = "0";
    public string StartPositionText
    {
        get => _startPositionText;
        set => SetProperty(ref _startPositionText, value);
    }

    private string _endPositionText = "0";
    public string EndPositionText
    {
        get => _endPositionText;
        set => SetProperty(ref _endPositionText, value);
    }

    public ICommand CmdMoveIncMinus { get; }
    public ICommand CmdMoveIncPlus { get; }
    public ICommand CmdServoOn { get; }
    public ICommand CmdServoOff { get; }
    public ICommand CmdAlarmClear { get; }
    public ICommand CmdHome { get; }
    public ICommand CmdMoveAbs { get; }
    public ICommand CmdMoveToStart { get; }
    public ICommand CmdMoveToEnd { get; }
    public ICommand CmdStartRepeat { get; }
    public ICommand CmdStopRepeat { get; }

    public MotorControlViewModel()
    {
        // NOTE:
        // - Attach(IMotorCommands)로 backend(프로파일)를 주입하면 실제 EtherCAT 명령으로 동작
        // - backend가 없으면(테스트 단계) UI 동작 확인용 더미로만 동작

        CmdMoveIncMinus = new RelayCommand(DoMoveIncMinus);
        CmdMoveIncPlus = new RelayCommand(DoMoveIncPlus);

        CmdServoOn = new RelayCommand(DoServoOn);
        CmdServoOff = new RelayCommand(DoServoOff);

        CmdAlarmClear = new RelayCommand(DoAlarmClear);
        CmdHome = new RelayCommand(DoHome);

        CmdMoveAbs = new RelayCommand(DoMoveAbs);
        CmdMoveToStart = new RelayCommand(DoMoveToStart);
        CmdMoveToEnd = new RelayCommand(DoMoveToEnd);

        CmdStartRepeat = new RelayCommand(() => { /* TODO */ });
        CmdStopRepeat = new RelayCommand(() => { /* TODO */ });
    }

    public void UiTick()
    {
        // 1) 스냅샷+맵 기반으로 6064(Actual Position) 읽기
        if (_motor == null) return;

        CurrentPosition = _motor.ActualPosition;
        //IsServoOn = _motor.IsServoOn;
        //HasError = _motor.IsError;
        //IsHomed = _motor.IsHome;
        //IsInPosition = _motor.IsInPosition;


    }


    public void Attach(IMotorCommands motor)
    {
        _motor = motor;

        // 초기 표기값 동기화
        if (_motor != null)
        {
            AxisName = "Axis " + _motor.AxisID;
            RefreshFromMotor();
        }


    }

    public void RefreshFromMotor()
    {
        if (_motor == null)
            return;

        //IsServoOn = _motor.IsServoOn;
        //IsHomed = _motor.IsHome;
        //HasError = _motor.IsError;

        CurrentPosition = _motor.ActualPosition;
    }

    private void DoServoOn()
    {
        if (_motor == null)
        {
            IsServoOn = true;
            return;
        }

        _motor.ServoOn();
        RefreshFromMotor();
    }

    private void DoServoOff()
    {
        if (_motor == null)
        {
            IsServoOn = false;
            return;
        }

        _motor.ServoOff();
        RefreshFromMotor();
    }

    private void DoAlarmClear()
    {
        if (_motor == null)
        {
            HasError = false;
            return;
        }

        _motor.AlarmClear();
        RefreshFromMotor();
    }

    private void DoHome()
    {
        if (_motor == null)
        {
            IsHomed = true;
            return;
        }

        _motor.Home();
        RefreshFromMotor();
    }

    private void DoMoveIncMinus()
    {
        if (!int.TryParse(IncDistanceText, out var inc))
            return;

        if (_motor == null)
        {
            CurrentPosition -= inc;
            IsInPosition = true;
            return;
        }

        _motor.MoveINC(-inc);
        RefreshFromMotor();
    }

    private void DoMoveIncPlus()
    {
        if (!int.TryParse(IncDistanceText, out var inc))
            return;

        if (_motor == null)
        {
            CurrentPosition += inc;
            IsInPosition = true;
            return;
        }

        _motor.MoveINC(inc);
        RefreshFromMotor();
    }

    private void DoMoveAbs()
    {
        if (!int.TryParse(AbsPositionText, out var pos))
            return;

        if (_motor == null)
        {
            CurrentPosition = pos;
            IsInPosition = true;
            return;
        }

        _motor.MoveABS(pos);
        RefreshFromMotor();
    }

    private void DoMoveToStart()
    {
        if (!int.TryParse(StartPositionText, out var pos))
            return;

        if (_motor == null)
        {
            CurrentPosition = pos;
            IsInPosition = true;
            return;
        }

        _motor.MoveABS(pos);
        RefreshFromMotor();
    }

    private void DoMoveToEnd()
    {
        if (!int.TryParse(EndPositionText, out var pos))
            return;

        if (_motor == null)
        {
            CurrentPosition = pos;
            IsInPosition = true;
            return;
        }

        _motor.MoveABS(pos);
        RefreshFromMotor();
    }
}
