using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SOEM_FrontEnd.Ethercat.MiniENI;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(MiniENI))]
[JsonSerializable(typeof(EniAdapterConfig))]
[JsonSerializable(typeof(EniSlaveConfig))]
[JsonSerializable(typeof(EniStartupSdo))]
[JsonSerializable(typeof(EniPdoMappingConfig))]
[JsonSerializable(typeof(EniPdoMapObject))]
[JsonSerializable(typeof(EniPdoMapEntry))]
[JsonSerializable(typeof(EniValidationResult))]
[JsonSerializable(typeof(List<EniSlaveConfig>))]
[JsonSerializable(typeof(List<EniStartupSdo>))]
[JsonSerializable(typeof(List<EniPdoMapObject>))]
[JsonSerializable(typeof(List<EniPdoMapEntry>))]
[JsonSerializable(typeof(List<string>))]
public partial class MiniENIJsonContext : JsonSerializerContext
{
}
