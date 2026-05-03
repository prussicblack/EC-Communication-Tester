using SOEM_FrontEnd.Ethercat.EthercatProfile.Interfaces;
using System.Text.Json.Serialization;

namespace SOEM_FrontEnd.NetMQ
{
    [JsonSourceGenerationOptions(WriteIndented = false, IncludeFields = true)]
    [JsonSerializable(typeof(TelemetryFrame))]
    [JsonSerializable(typeof(SlaveTelemetryFrame))]
    [JsonSerializable(typeof(SlaveTelemetryFrame[]))]
    [JsonSerializable(typeof(RawPdoTelemetryFrame))]
    [JsonSerializable(typeof(MotorTelemetryFrame))]
    [JsonSerializable(typeof(ValueSnapshotFrame))]
    [JsonSerializable(typeof(ValueChannelSnapshot))]
    [JsonSerializable(typeof(ValueChannelSnapshot[]))]
    public partial class TelemetryJsonContext : JsonSerializerContext
    {
    }
}
