using SOEM_FrontEnd.DataMap;
using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;

namespace SOEM_FrontEnd.NetMQ
{
    public sealed class TelemetrySnapshotProvider
    {
        private long _sequence;

        public TelemetryFrame CreateFrame()
        {
            TelemetryFrame frame = new TelemetryFrame();
            frame.Sequence = Interlocked.Increment(ref _sequence);
            frame.UnixTimeMs = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            if (Datamap.Instance.IsInit() == false)
            {
                return frame;
            }

            int slaveCount = Datamap.Instance.SlaveCount;
            List<SlaveTelemetryFrame> slaves = new List<SlaveTelemetryFrame>();

            for (int slaveNo = 1; slaveNo < slaveCount; slaveNo++)
            {
                SlaveTelemetryFrame slaveFrame = CreateSlaveFrame(slaveNo);

                if (slaveFrame != null)
                {
                    slaves.Add(slaveFrame);
                }
            }

            frame.Slaves = slaves.ToArray();
            return frame;
        }

        private static SlaveTelemetryFrame CreateSlaveFrame(int slaveNo)
        {
            SlaveStore store;

            try
            {
                store = Datamap.Instance.GetSlave(slaveNo);
            }
            catch
            {
                return null;
            }

            if (store == null)
            {
                return null;
            }

            object profile = store.BaseProfile;

            if (profile == null)
            {
                return null;
            }

            SlaveTelemetryFrame slaveFrame = new SlaveTelemetryFrame();
            slaveFrame.SlaveNo = slaveNo;
            slaveFrame.ProfileKind = ResolveProfileKind(profile);

            IPDOView pdoView = profile as IPDOView;
            if (pdoView != null)
            {
                slaveFrame.RawPdo = CreateRawPdoFrame(pdoView);
            }

            IValuePdoView valueView = profile as IValuePdoView;
            if (valueView != null)
            {
                slaveFrame.Value = valueView.GetValueSnapshot();
            }

            IMotorCommands motor = profile as IMotorCommands;
            if (motor != null)
            {
                slaveFrame.Motor = CreateMotorFrame(slaveNo, motor);
            }

            return slaveFrame;
        }

        private static string ResolveProfileKind(object profile)
        {
            if (profile is IMotorCommands)
            {
                return "PPMode";
            }

            if (profile is IValuePdoView)
            {
                return "Value";
            }

            if (profile is IPDOView)
            {
                return "IO";
            }

            return "Unknown";
        }

        private static RawPdoTelemetryFrame CreateRawPdoFrame(IPDOView pdoView)
        {
            ReadOnlyMemory<byte> output = pdoView.OutputSnapshot;
            ReadOnlyMemory<byte> input = pdoView.InputSnapshot;

            RawPdoTelemetryFrame frame = new RawPdoTelemetryFrame();
            frame.RxOutputBytes = output.Length;
            frame.TxInputBytes = input.Length;
            frame.RxOutputHex = ToHex(output.Span);
            frame.TxInputHex = ToHex(input.Span);

            return frame;
        }

        private static MotorTelemetryFrame CreateMotorFrame(int slaveNo, IMotorCommands motor)
        {
            MotorTelemetryFrame frame = new MotorTelemetryFrame();
            frame.SlaveNo = slaveNo;

            // Publish에서는 IMotorCommands의 상태 property만 읽는다.
            // Move/ServoOn/AlarmClear 같은 command method는 여기서 호출하지 않는다.
            frame.IsServoOn = motor.IsServoOn;
            frame.IsInPosition = motor.IsInPosition;
            frame.IsHome = motor.IsHome;
            frame.IsError = motor.IsError;
            frame.ActualPosition = motor.ActualPosition;
            frame.IsHomeSensor = motor.IsHomeSensor;
            frame.IsNLimSensor = motor.IsNLimSensor;
            frame.IsPLimSensor = motor.IsPLimSensor;

            return frame;
        }

        private static string ToHex(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0)
            {
                return "";
            }

            char[] chars = new char[data.Length * 2];

            for (int i = 0; i < data.Length; i++)
            {
                byte value = data[i];

                chars[i * 2] = GetHexChar(value >> 4);
                chars[i * 2 + 1] = GetHexChar(value & 0x0F);
            }

            return new string(chars);
        }

        private static char GetHexChar(int value)
        {
            if (value < 10)
            {
                return (char)('0' + value);
            }

            return (char)('A' + value - 10);
        }
    }
}
