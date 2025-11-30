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
                    foreach (var profileNoitem in profileNo)
                        device.ProfileNo.Add(ParseProfileNo(profileNoitem, ns));
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

            // RxPDO
            var rxRoot = dev.Element(ns + "RxPdo");
            if (rxRoot != null)
            {
                foreach (var pdoElem in rxRoot.Elements(ns + "Pdo"))
                    device.RxPdos.Add(ParsePdo(pdoElem, ns));
            }

            // TxPDO
            var txRoot = dev.Element(ns + "TxPdo");
            if (txRoot != null)
            {
                foreach (var pdoElem in txRoot.Elements(ns + "Pdo"))
                    device.TxPdos.Add(ParsePdo(pdoElem, ns));
            }

            // DC
            var DCRoot = dev.Element(ns + "DC");
            if (DCRoot != null)
            {
                //foreach (var objElem in DCRoot.Elements(ns + "DC"))
                //    device.DC.Add(ParseDCObject(objElem, ns));
            }

            return device;
        }

        //여기부터?
        private static string ParseProfileNo(XElement profileNoitem, XNamespace ns)
        {
            throw new NotImplementedException();
        }

        private static ESIPDO ParsePdo(XElement pdoElem, XNamespace ns)
        {
            var indexAttr = pdoElem.Attribute("Index");
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

                pdo.Entries.Add(new ESIPDOEntry
                {
                    Index = (ushort)ParseUint(eIdx.Value),
                    SubIndex = (byte)ParseUint(eSub.Value),
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
    }
}
