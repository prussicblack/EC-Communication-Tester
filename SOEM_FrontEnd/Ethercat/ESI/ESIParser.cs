using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static SOEM_FrontEnd.Ethercat.ESI.ESIXMLData;

namespace SOEM_FrontEnd.Ethercat.ESI
{
    public static class EsiParser
    {
        public static EsiFile Parse(string path)
        {
            var doc = XDocument.Load(path);
            var root = doc.Root;
            if (root == null)
                throw new Exception("Invalid ESI: no root element");

            XNamespace ns = root.Name.Namespace;

            // Vendor
            var vendorElem = root.Element(ns + "Vendor");
            if (vendorElem == null)
                throw new Exception("No <Vendor> element");

            var vendorIdElem = vendorElem.Element("Id");
            if (vendorIdElem == null)
                throw new Exception("Vendor Id attribute missing");

            var vendorImageData = vendorElem.Element("ImageData16x14");
            if (vendorImageData == null)
            {
                //굳이 없어도 됨.
            }


            uint vendorId = ParseUint(vendorIdElem.Value);
            string vendorName = (string)vendorElem.Attribute("Name") ?? "";
            string ImageData = (string)vendorElem.Attribute("ImageData16x14") ?? "";

            var esi = new EsiFile
            {
                VendorId = vendorId,
                VendorName = vendorName,
                ImageData = ImageData
            };


            // Devices
            var descriptions = root.Element(ns + "Descriptions");
            if (descriptions == null)
                throw new Exception("No <Descriptions>");

            var devicesElem = descriptions.Element(ns + "Devices");
            if (devicesElem == null)
                throw new Exception("No <Devices>");

            foreach (var devElem in devicesElem.Elements(ns + "Device"))
            {
                esi.Devices.Add(ParseDevice(devElem, ns, vendorId));
            }

            return esi;
        }

        private static ESIDevice ParseDevice(XElement dev, XNamespace ns, uint vendorId)
        {
            var type = dev.Element(ns + "Type");
            if (type == null)
                throw new Exception("No <Type> in <Device>");

            var productAttr = type.Attribute("ProductCode");
            var revisionAttr = type.Attribute("RevisionNo");
            if (productAttr == null || revisionAttr == null)
                throw new Exception("Type missing ProductCode or RevisionNo");

            uint product = ParseUint(productAttr.Value);
            uint revision = ParseUint(revisionAttr.Value);

            var profile = dev.Element(ns + "Profile");
            if (profile == null)
            {
                //Servo가 아닌경우는 프로파일이 없을 수 있음.
            }

            var device = new ESIDevice
            {
                VendorId = vendorId, //나중에 사용을 용이하게 하기 위해 추가.
                ProductCode = product,
                Revision = revision,
                Name = (string)dev.Element(ns + "Name") ?? "",
                //GroupType = (string)dev.Element(ns + "GroupType") ?? "",
            };

            //profile - profileNo
            if (profile != null)
            {
                var profileNo = profile.Elements(ns + "ProfileNo");
                if (profileNo != null)
                {
                    //foreach (var profileNoitem in profileNo)
                    //    device.ProfileNo.Add(ParseProfileNo(profileNoitem, ns));
                }

                //Profile - DiagMessages
                var diagmessages = profile.Elements(ns + "DiagMessages");
                if (diagmessages != null)
                {
                    //foreach (var diagmsg in diagmessages)
                    //device.DiagMessages.Add(ParseDiagMessage(diagmsg, ns));
                }

                //Profile - Dictionary
                var dictionary = profile.Element(ns + "Dictionary");
                //Profile - Dictionary - DataTypes
                if (dictionary != null)
                {
                    //foreach (var datatype in dictionary.Elements(ns + "DataTypes"))
                    //device.Datatypes.Add(ParseDataTypes(datatype, ns));

                    //foreach (var SDOs in dictionary.Elements(ns + "Objects"))
                    //device.SDOObjects.Add(ParseSDOObjects(SDOs, ns));
                }
            }
            
            //이하부 테스트 필요.
            var SmRoot = dev.Element(ns + "Sm");
            if (SmRoot != null)
            {
                device.Sm.AddRange(ParseSm(dev, ns));
            }

            // RxPDO
            var rxRoot = dev.Element(ns + "RxPdo");
            if (rxRoot != null)
            {
                //우선 해야될것.
                device.RxPdos.Add(ParsePdo(rxRoot, ns));
            }

            // TxPDO
            var txRoot = dev.Element(ns + "TxPdo");
            if (txRoot != null)
            {
                device.TxPdos.Add(ParsePdo(txRoot, ns));
            }

            // DC
            var DCRoot = dev.Element(ns + "DC");
            if (DCRoot != null)
            {
                foreach (var objElem in DCRoot.Elements(ns + "OpMode"))
                    device.DC.Add(ParseDCObject(DCRoot, ns));
            }

            return device;
        }

        private static string ParseProfileNo(XElement profileNoitem, XNamespace ns)
        {
            throw new NotImplementedException();
        }

        private static ESIDcObject ParseDCObject(XElement OPMode, XNamespace ns)
        {
            ESIDcObject dc = new ESIDcObject();

            var Name = OPMode.Element(ns + "Name");
            var Desc = OPMode.Element(ns + "Desc");
            var AssignActivate = OPMode.Element(ns + "AssignActivate");
            var CycleTimeSync0 = OPMode.Element(ns + "CycleTimeSync0");
            var ShiftTimeSync0 = OPMode.Element(ns + "ShiftTimeSync0");
            var CycleTimeSync1 = OPMode.Element(ns + "CycleTimeSync1");
            var ShiftTimeSync1 = OPMode.Element(ns + "ShiftTimeSync1");

            dc.Name = (string)Name ?? "";
            dc.Desc = (string)Desc ?? "";
            dc.AssignActivate = (ushort)(AssignActivate != null ? ParseUint(AssignActivate.Value) : 0);

            // CycleTimeSync0
            if (CycleTimeSync0 != null)
            {
                dc.CycleTimeSync0 = new CycleTimeSync0();
                var factorAttr = CycleTimeSync0.Attribute("Factor");
                dc.CycleTimeSync0.Factor = (short)(factorAttr != null ? ParseLong(factorAttr.Value) : 0);
                var valueAttr = CycleTimeSync0.Attribute("Value");
                dc.CycleTimeSync0.Value = (short)(valueAttr != null ? ParseLong(valueAttr.Value) : 0);
            }

            // ShiftTimeSync0
            if (ShiftTimeSync0 != null)
            {
                dc.ShiftTimeSync0 = new ShiftTimeSync0();
                var inputAttr = ShiftTimeSync0.Attribute("Input");
                dc.ShiftTimeSync0.Input = (short)(inputAttr != null ? ParseLong(inputAttr.Value) : 0);
                var valueAttr = ShiftTimeSync0.Attribute("Value");
                dc.ShiftTimeSync0.Value = (short)(valueAttr != null ? ParseLong(valueAttr.Value) : 0);
            }

            // CycleTimeSync1
            if (CycleTimeSync1 != null)
            {
                dc.CycleTimeSync1 = new CycleTimeSync1();
                var factorAttr = CycleTimeSync1.Attribute("Factor");
                dc.CycleTimeSync1.Factor = (short)(factorAttr != null ? ParseLong(factorAttr.Value) : 0);
                var valueAttr = CycleTimeSync1.Attribute("Value");
                dc.CycleTimeSync1.Value = (short)(valueAttr != null ? ParseLong(valueAttr.Value) : 0);
            }

            // ShiftTimeSync1
            if (ShiftTimeSync1 != null)
            {
                dc.ShiftTimeSync1 = new ShiftTimeSync1();
                var inputAttr = ShiftTimeSync1.Attribute("Input");
                dc.ShiftTimeSync1.Input = (short)(inputAttr != null ? ParseLong(inputAttr.Value) : 0);
                var valueAttr = ShiftTimeSync1.Attribute("Value");
                dc.ShiftTimeSync1.Value = (short)(valueAttr != null ? ParseLong(valueAttr.Value) : 0);
            }

            return dc;
        }






        private static List<ESISyncManager> ParseSm(XElement dev, XNamespace ns)
        {
            List<ESISyncManager> ret = new List<ESISyncManager>();
            ushort index = 0;

            foreach (var smElem in dev.Elements(ns + "Sm"))
            {
                var sm = new ESISyncManager();
                sm.Index = index++;

                var minSizeAttr = smElem.Attribute("MinSize");
                var maxSizeAttr = smElem.Attribute("MaxSize");
                var defaultSizeAttr = smElem.Attribute("DefaultSize");
                var startAddrAttr = smElem.Attribute("StartAddress");
                var controlAttr =smElem.Attribute("ControlByte");
                var enableAttr = smElem.Attribute("Enable");

                var nameText = smElem.Value;

                if (nameText == null)
                {
                    nameText = string.Empty;
                }
                sm.Name = nameText.Trim();

                sm.MinSize = (ushort)ParseUint(minSizeAttr.Value);
                sm.MaxSize = (ushort)ParseUint(maxSizeAttr.Value);
                sm.DefaultSize = (ushort)ParseUint(defaultSizeAttr.Value);
                sm.ControlByte = (ushort)ParseUint(startAddrAttr.Value);
                sm.Enable = (ushort)ParseUint(controlAttr.Value);
                sm.StartAddress = (ushort)ParseUint(enableAttr.Value);

                ret.Add(sm);
            }

            return ret;
        }



        private static ESIPDO ParsePdo(XElement pdoElem, XNamespace ns)
        {
            var indexAttr = pdoElem.Element("Index");
            if (indexAttr == null)
                throw new Exception("Pdo missing Index attribute");

            ushort index = (ushort)ParseUint(indexAttr.Value);
            string name = (string)pdoElem.Element(ns + "Name") ?? "";

            var pdo = new ESIPDO
            {
                Index = index,
                Name = name
            };

            var smAttr = pdoElem.Attribute("Sm");
            if (smAttr != null)
            {
                int sm;
                if (int.TryParse(smAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out sm))
                    pdo.Sm = sm;
            }

            var fixedAttr = pdoElem.Attribute("Fixed");
            if (fixedAttr != null)
            {
                int fixedval;
                if (int.TryParse(fixedAttr.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fixedval))
                    pdo.Fixed = fixedval;
            }

            foreach (var e in pdoElem.Elements(ns + "Entry"))
            {
                var eIdx = e.Element(ns + "Index");
                var eSub = e.Element(ns + "SubIndex");
                var eBits = e.Element(ns + "BitLen");
                //if (eIdx == null || eSub == null || eBits == null)
                //    continue;

                byte subIndex;

                if (eSub == null || string.IsNullOrWhiteSpace(eSub.Value))
                {
                    subIndex = 0;
                }
                else
                {
                    subIndex = (byte)ParseUint(eSub.Value);
                }


                pdo.Entries.Add(new ESIPDOEntry
                {
                    Index = (ushort)ParseUint(eIdx.Value),
                    SubIndex = subIndex,
                    BitLength = (int)ParseUint(eBits.Value),
                    Name = (string)e.Element(ns + "Name") ?? "",
                    DataType = (string)e.Element(ns + "DataType") ?? ""
                });
            }

            return pdo;
        }

        private static uint ParseUint(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;

            s = s.Trim();

            if (s.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);

            // 헥스 문자 포함되면 16진수로 처리
            if (s.IndexOfAny("ABCDEFabcdef".ToCharArray()) >= 0)
                return uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return uint.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        // ParseLong 메서드 추가
        private static long ParseLong(string s)
        {
            if (string.IsNullOrEmpty(s))
                return 0;

            s = s.Trim();

            if (s.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);

            // 헥스 문자 포함되면 16진수로 처리
            if (s.IndexOfAny("ABCDEFabcdef".ToCharArray()) >= 0)
                return long.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }


    }
}
