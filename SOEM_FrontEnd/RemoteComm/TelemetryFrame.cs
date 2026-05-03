using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;

namespace SOEM_FrontEnd.NetMQ
{
    public sealed class TelemetryFrame
    {
        public long Sequence { get; set; }

        public long UnixTimeMs { get; set; }

        public SlaveTelemetryFrame[] Slaves { get; set; }

        public TelemetryFrame()
        {
            Slaves = new SlaveTelemetryFrame[0];
        }
    }

    public sealed class SlaveTelemetryFrame
    {
        public int SlaveNo { get; set; }

        public string ProfileKind { get; set; }

        public RawPdoTelemetryFrame RawPdo { get; set; }

        public ValueSnapshotFrame Value { get; set; }

        public MotorTelemetryFrame Motor { get; set; }

        public SlaveTelemetryFrame()
        {
            ProfileKind = "";
        }
    }

    public sealed class RawPdoTelemetryFrame
    {
        // RxPDO: master -> slave, SOEM output image
        public string RxOutputHex { get; set; }

        // TxPDO: slave -> master, SOEM input image
        public string TxInputHex { get; set; }

        public int RxOutputBytes { get; set; }

        public int TxInputBytes { get; set; }

        public RawPdoTelemetryFrame()
        {
            RxOutputHex = "";
            TxInputHex = "";
        }
    }

    public sealed class MotorTelemetryFrame
    {
        public int SlaveNo { get; set; }

        public bool IsServoOn { get; set; }

        public bool IsInPosition { get; set; }

        public bool IsHome { get; set; }

        public bool IsError { get; set; }

        public int ActualPosition { get; set; }

        public bool IsHomeSensor { get; set; }

        public bool IsNLimSensor { get; set; }

        public bool IsPLimSensor { get; set; }
    }
}
