using System.Text.Json;

namespace SOEM_FrontEnd.Ethercat.MiniENI;

//Native AOT를 위한 Json 클래스.
//System.Text.Json은 리플렉션을 쓰는 관계로 AOT에서 사용이 불가능함.

public static class MiniENIJson
{
    public static string Serialize(MiniENI eni)
    {
        return JsonSerializer.Serialize(
            eni,
            MiniENIJsonContext.Default.MiniENI);
    }

    public static MiniENI Deserialize(string json)
    {
        MiniENI? eni = JsonSerializer.Deserialize(
            json,
            MiniENIJsonContext.Default.MiniENI);

        if (eni == null)
        {
            return new MiniENI();
        }

        Normalize(eni);

        return eni;
    }

    public static void Normalize(MiniENI eni)
    {
        if (eni.Adapter == null)
        {
            eni.Adapter = new EniAdapterConfig();
        }

        if (eni.Slaves == null)
        {
            eni.Slaves = new System.Collections.Generic.List<EniSlaveConfig>();
        }

        for (int i = 0; i < eni.Slaves.Count; i++)
        {
            EniSlaveConfig slave = eni.Slaves[i];

            if (slave.StartupSdos == null)
            {
                slave.StartupSdos = new System.Collections.Generic.List<EniStartupSdo>();
            }

            if (slave.PdoMapping == null)
            {
                slave.PdoMapping = new EniPdoMappingConfig();
            }

            if (slave.PdoMapping.RxAssign == null)
            {
                slave.PdoMapping.RxAssign = new System.Collections.Generic.List<string>();
            }

            if (slave.PdoMapping.TxAssign == null)
            {
                slave.PdoMapping.TxAssign = new System.Collections.Generic.List<string>();
            }

            if (slave.PdoMapping.RxMaps == null)
            {
                slave.PdoMapping.RxMaps = new System.Collections.Generic.List<EniPdoMapObject>();
            }

            if (slave.PdoMapping.TxMaps == null)
            {
                slave.PdoMapping.TxMaps = new System.Collections.Generic.List<EniPdoMapObject>();
            }
        }
    }
}
