using CommunityToolkit.Mvvm.Input;
using SOEM_FrontEnd.Ethercat;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System.Windows.Input;

namespace SOEM_FrontEnd.ViewModels;

public partial class MotorControlViewModel : ViewModelBase
{
    private IMotorCommands _motor; // backend profile 

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

    private string _speed = "1000";
    public string Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }

    private string _accelText = "100";
    public string AccelText
    {
        get => _accelText;
        set => SetProperty(ref _accelText, value);
    }

    private string _decelText = "100";
    public string DecelText
    {
        get => _decelText;
        set => SetProperty(ref _decelText, value);
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


    private string _homeMethodText = "37";
    public string HomeMethodText
    {
        get { return _homeMethodText; }
        set { SetProperty(ref _homeMethodText, value); }
    }

    private string _homeSearchSwitchSpeedText = "10";
    public string HomeSearchSwitchSpeedText
    {
        get { return _homeSearchSwitchSpeedText; }
        set { SetProperty(ref _homeSearchSwitchSpeedText, value); }
    }

    private string _homeSearchZeroSpeedText = "5";
    public string HomeSearchZeroSpeedText
    {
        get { return _homeSearchZeroSpeedText; }
        set { SetProperty(ref _homeSearchZeroSpeedText, value); }
    }

    private string _homeAccelerationText = "100";
    public string HomeAccelerationText
    {
        get { return _homeAccelerationText; }
        set { SetProperty(ref _homeAccelerationText, value); }
    }

    private string _homeOffsetText = "0";
    public string HomeOffsetText
    {
        get { return _homeOffsetText; }
        set { SetProperty(ref _homeOffsetText, value); }
    }




    private bool _nlim;
    public bool nlim
    {
        get => _nlim;
        set => SetProperty(ref _nlim, value);
    }

    private bool _plim;
    public bool plim
    {
        get => _plim;
        set => SetProperty(ref _plim, value);
    }

    private bool _org;
    public bool org
    {
        get => _org;
        set => SetProperty(ref _org, value);
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

    public ICommand CmdMoveStop { get; }
   

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

        CmdMoveStop = new RelayCommand(DoMoveStop);

        CmdMoveToStart = new RelayCommand(DoMoveToStart);
        CmdMoveToEnd = new RelayCommand(DoMoveToEnd);

        CmdStartRepeat = new RelayCommand(() => { /* 할거...*/ });
        CmdStopRepeat = new RelayCommand(() => { /* 할거...*/ });

    }

    public void UiTick()
    {
        //스냅샷+맵 기반으로 6064(Actual Position) 읽기
        if (_motor == null) return;

        CurrentPosition = _motor.ActualPosition;
        IsServoOn = _motor.IsServoOn;
        HasError = _motor.IsError;
        IsHomed = _motor.IsHome;
        IsInPosition = _motor.IsInPosition;

        org = _motor.IsHomeSensor;
        nlim = _motor.IsNLimSensor;
        plim = _motor.IsPLimSensor;
    }


    public void Attach(IMotorCommands motor)
    {
        _motor = motor;

        // 초기 표기값 동기화
        if (_motor != null)
        {
            //AxisName = "Axis " + _motor.AxisID;
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

        sbyte method;
        uint searchSwitchSpeed;
        uint searchZeroSpeed;
        uint acceleration;
        int homeOffset;

        if (!sbyte.TryParse(HomeMethodText, out method))
            return;

        if (!uint.TryParse(HomeSearchSwitchSpeedText, out searchSwitchSpeed))
            return;

        if (!uint.TryParse(HomeSearchZeroSpeedText, out searchZeroSpeed))
            return;

        if (!uint.TryParse(HomeAccelerationText, out acceleration))
            return;

        if (!int.TryParse(HomeOffsetText, out homeOffset))
            return;

        _motor.SetHomeProfile(method, searchSwitchSpeed, searchZeroSpeed, acceleration, homeOffset);

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

        _motor.SetProfile(uint.Parse(Speed), uint.Parse(AccelText), uint.Parse(DecelText));

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
        
        _motor.SetProfile(uint.Parse(Speed), uint.Parse(AccelText), uint.Parse(DecelText));

        _motor.MoveINC(inc);
        RefreshFromMotor();
    }


    public void JogPlusPressed()
    {
        if (_motor == null)
        {
            return;
        }

        _motor.SetProfile(uint.Parse(Speed), uint.Parse(AccelText), uint.Parse(DecelText));

        _motor.JogPlus();
        RefreshFromMotor();
    }

    public void JogMinusPressed()
    {
        if (_motor == null)
        {
            return;
        }

        _motor.SetProfile(uint.Parse(Speed), uint.Parse(AccelText), uint.Parse(DecelText));

        _motor.JogMinus();
        RefreshFromMotor();
    }

    public void JogReleased()
    {
        if (_motor == null)
            return;

        _motor.JogStop();
    }


    private void DoMoveStop()
    {
        if (_motor == null)
        {
            IsInPosition = true;
            return;
        }

        _motor.Stop();
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

        _motor.SetProfile(uint.Parse(Speed) , uint.Parse(AccelText), uint.Parse(DecelText));

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

        _motor.SetProfile(uint.Parse(Speed), uint.Parse(AccelText), uint.Parse(DecelText));

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

        _motor.SetProfile(uint.Parse(Speed), uint.Parse(AccelText), uint.Parse(DecelText));

        _motor.MoveABS(pos);
        RefreshFromMotor();
    }
}
