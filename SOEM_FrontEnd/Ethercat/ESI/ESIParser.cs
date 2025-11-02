using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static SOEM_FrontEnd.Ethercat.ESI.ESIXMLData;

namespace SOEM_FrontEnd.Ethercat.ESI
{
    public static class ESIParser
    {
        public static EsiDevice ParseDevice(string esiPath, uint vendorId, uint productCode, uint revisionNo)
        {
            var doc = XDocument.Load(esiPath);
            var xDesc = doc.Root.Element("Descriptions");
            if (xDesc == null) 
                throw new InvalidDataException("Descriptions not found");

            var xDevices = xDesc.Element("Devices");
            if (xDevices == null) 
                throw new InvalidDataException("Devices not found");

            var xDevice = xDevices.Elements("Device")
                .FirstOrDefault(d =>
                    ReadUInt(d.Element("Type")?.Element("VendorId")) == vendorId &&
                    ReadUInt(d.Element("Type")?.Element("ProductCode")) == productCode &&
                    MatchRevision(d, revisionNo));

            if (xDevice == null) 
                throw new KeyNotFoundException("Device not found for given IDs");

            var dev = new EsiDevice();
            dev.VendorId = vendorId;
            dev.ProductCode = productCode;
            dev.RevisionNo = revisionNo;
            dev.Name = (string)xDevice.Element("Name") ?? "";

            // CoE
            var xCoe = xDevice.Element("Profile")?.Element("CoE");
            dev.Coe.Enabled = xCoe != null;
            if (xCoe != null)
            {
                dev.Coe.SdoInfo = ReadBool(xCoe.Element("SdoInfo"));
                dev.Coe.PdoAssign = ReadBool(xCoe.Element("PdoAssign"));
                dev.Coe.PdoConfig = ReadBool(xCoe.Element("PdoConfig"));
                dev.Coe.PdoUpload = ReadBool(xCoe.Element("PdoUpload"));
            }

            // DC
            var xDc = xDevice.Element("Dc");
            if (xDc != null)
            {
                dev.Dc.Supported = true;
                dev.Dc.CycleTime0Ns = ReadLong(xDc.Element("CycleTime0"));
                dev.Dc.CycleTime1Ns = ReadLong(xDc.Element("CycleTime1"));
                dev.Dc.ShiftNs = ReadLong(xDc.Element("Shift"));
                dev.Dc.AssignActivate = ReadUShort(xDc.Element("AssignActivate"));
            }

            // RxPDO / TxPDO
            var xRx = xDevice.Descendants("RxPdo");
            foreach (var xp in xRx)
            {
                dev.RxPdos.Add(ParsePdo(xp));
            }

            var xTx = xDevice.Descendants("TxPdo");
            foreach (var xp in xTx)
            {
                dev.TxPdos.Add(ParsePdo(xp));
            }

            // Dictionary Objects
            var xDict = xDevice.Element("Dictionary") ?? xDevice.Element("Device")?.Element("Dictionary");
            if (xDict != null)
            {
                var xObjs = xDict.Descendants("Object");
                foreach (var xo in xObjs)
                {
                    dev.Objects.Add(ParseObject(xo));
                }
            }

            return dev;
        }

        private static Pdo ParsePdo(XElement xPdo)
        {
            var p = new Pdo();
            p.Index = ReadUShort(xPdo.Element("Index")) ?? (ushort)0;
            p.Name = (string)xPdo.Element("Name") ?? "";
            p.Default = ReadBool(xPdo.Element("Default"));

            foreach (var xe in xPdo.Elements("Entry"))
            {
                var e = new PdoEntry();
                e.Index = ReadUShort(xe.Element("Index")) ?? (ushort)0;
                e.SubIndex = (byte)(ReadByte(xe.Element("SubIndex")) ?? 0);
                e.BitLen = (int)(ReadInt(xe.Element("BitLen")) ?? 0);
                e.Name = (string)xe.Element("Name") ?? "";
                p.Entries.Add(e);
            }
            return p;
        }

        private static EcatObject ParseObject(XElement xo)
        {
            var o = new EcatObject();
            o.Index = ReadUShort(xo.Element("Index")) ?? (ushort)0;
            o.Name = (string)xo.Element("Name") ?? "";
            o.DataType = (string)xo.Element("DataType") ?? "";
            o.Access = (string)xo.Element("Access") ?? "";
            o.Default = (string)xo.Element("Default");
            var xm = xo.Element("PdoMapping");
            if (xm != null) o.PdoMapping = ReadBool(xm);

            foreach (var xs in xo.Elements("SubItem"))
            {
                var s = new EcatSubItem();
                s.SubIndex = (byte)(ReadByte(xs.Element("SubIndex")) ?? 0);
                s.Name = (string)xs.Element("Name") ?? "";
                s.DataType = (string)xs.Element("DataType") ?? "";
                s.Access = (string)xs.Element("Access") ?? "";
                s.Default = (string)xs.Element("Default");
                var sm = xs.Element("PdoMapping");
                if (sm != null) s.PdoMapping = ReadBool(sm);
                o.Subs.Add(s);
            }
            return o;
        }

        // ---------- helpers ----------
        private static bool MatchRevision(XElement xDevice, uint rev)
        {
            var xType = xDevice.Element("Type");
            if (xType == null) 
                return true;
            var xRev = xType.Element("RevisionNo");
            if (xRev == null) 
                return true;
            var val = ReadUInt(xRev);
            if (val.HasValue) 
                return val.Value == rev;

            // 일부 ESI는 범위/마스크 표현을 씀 (간단 매칭만)
            var attrMin = (string)xRev.Attribute("Min");
            var attrMax = (string)xRev.Attribute("Max");
            if (!string.IsNullOrEmpty(attrMin) && !string.IsNullOrEmpty(attrMax))
            {
                uint min = ParseUInt(attrMin);
                uint max = ParseUInt(attrMax);
                return rev >= min && rev <= max;
            }
            return true;
        }

        private static uint? ReadUInt(XElement x)
        {
            if (x == null) 
                return null;
            uint v;
            if (TryParseUInt(x.Value, out v)) 
                return v;
            return null;
        }
        private static ushort? ReadUShort(XElement x)
        {
            if (x == null) 
                return null;
            ushort v;
            if (TryParseUShort(x.Value, out v)) 
                return v;
            return null;
        }
        private static long? ReadLong(XElement x)
        {
            if (x == null) 
                return null;
            long v;
            if (long.TryParse(x.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) 
                return v;
            return null;
        }
        private static int? ReadInt(XElement x)
        {
            if (x == null) 
                return null;
            int v;
            if (int.TryParse(x.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) 
                return v;
            return null;
        }
        private static byte? ReadByte(XElement x)
        {
            if (x == null) 
                return null;
            byte v;
            if (byte.TryParse(x.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) 
                return v;
            return null;
        }
        private static bool ReadBool(XElement x)
        {
            if (x == null) 
                return false;

            // "1/0", "true/false", "True/False" 모두 허용
            var s = x.Value.Trim();
            if (string.Equals(s, "1")) 
                return true;
            if (string.Equals(s, "0")) 
                return false;

            bool v;
            if (bool.TryParse(s, out v)) 
                return v;

            return false;
        }
        private static uint ParseUInt(string s)
        {
            uint v;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                v = uint.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return v;
            }
            v = uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

            return v;
        }
        private static bool TryParseUInt(string s, out uint v)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);

            return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }
        private static bool TryParseUShort(string s, out ushort v)
        {
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);

            return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }


    }
}
