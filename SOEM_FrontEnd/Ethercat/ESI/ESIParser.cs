using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
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
                    {
                        device.ProfileNo.Add(ParseUint(profileNoitem.Value));
                    }
                        
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
                if (dictionary != null)
                {
                    //DataType
                    var DataTypes = dictionary.Element(ns + "DataTypes");
                    List<ESIDataType> sdoDataTypes = ParseDataTypes(DataTypes, ns);
                    foreach (var datatype in sdoDataTypes)
                    {
                        device.Datatypes.Add(datatype.Name, datatype);
                    }

                    //SDO Object.
                    var Objects = dictionary.Element(ns + "Objects");
                    List<ESISDOObject> sdoObjects = ParseSDOObjects(Objects, ns);
                    foreach (var item in sdoObjects)
                    {
                        device.SDOObjects.Add(item.Index.ToString(), item);
                    }
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
                foreach(var rxpdoelem in dev.Elements(ns + "RxPdo"))
                    device.RxPdos.Add(ParsePdo(rxpdoelem, ns));
            }

            // TxPDO
            var txRoot = dev.Element(ns + "TxPdo");
            if (txRoot != null)
            {
                foreach(var txpdoelem in dev.Elements(ns + "TxPdo"))
                    device.TxPdos.Add(ParsePdo(txpdoelem, ns));
            }

            // DC
            var DCRoot = dev.Element(ns + "Dc");
            if (DCRoot != null)
            {
                foreach (var objElem in DCRoot.Elements(ns + "OpMode"))
                    device.Dc.Add(ParseDCObject(objElem, ns));
            }

            return device;
        }

        private static List<ESISDOObject> ParseSDOObjects(XElement ObjectsElement, XNamespace ns)
        {
            List<ESISDOObject> ret = new ();

            foreach (var objectElement in ObjectsElement.Elements(ns+"Object"))
            {

                var index = objectElement.Element("Index");
                var Name = objectElement.Element("Name");

                var Type = objectElement.Element("Type");
                var BitSize = objectElement.Element("BitSize");
                var Flags = objectElement.Element("Flags");

                Flags flags = new Flags();

                if (Flags != null)
                {
                    flags = ParseFlags(Flags, ns);
                }

                ESISDOObject sdoobject = new ESISDOObject();

                sdoobject.Index = (ushort)ParseUint(index.Value);
                sdoobject.Name = Name.Value ?? "";
                sdoobject.DataType = Type.Value ?? "";
                sdoobject.BitSize = (ushort)ParseUint(BitSize.Value);

                sdoobject.Flags = flags;

                ret.Add(sdoobject);
            }

            return ret;
        }

        private static List<ESIDataType> ParseDataTypes(XElement DataTypesElements, XNamespace ns)
        {
            var ret = new List<ESIDataType>();

            foreach (var DataTypesElement in DataTypesElements.Elements(ns + "DataType"))
            {
                var Name = DataTypesElement.Element(ns + "Name");
                var BitSize = DataTypesElement.Element(ns + "BitSize");
                var BaseType = DataTypesElement.Element(ns + "BaseType");
                var SubItems = DataTypesElement.Elements(ns + "SubItem");

                var SubItemTypes = new List<ESISubDataType>();

                //SubIdx 자동 할당용 상태 (DataType 단위)
                var used = new HashSet<int>();   // 이미 사용/예약된 SubIdx
                int lastAssigned = -1;           // 직전 확정 SubIdx

                // 명시된 SubIdx들을 예약
                foreach (var si in SubItems)
                {
                    var sidxEl = si.Element(ns + "SubIdx");
                    if (sidxEl != null)
                    {
                        used.Add((int)ParseUint(sidxEl.Value));
                    }
                }

                //SubIdx가 없을 때 호출될 로컬 함수
                byte SubIndexNothing()
                {
                    int cand;

                    // 직전 확정이 있으면 +1, 없으면 0부터(필요시 1부터로 바꿔도 됨)
                    if (lastAssigned >= 0) cand = lastAssigned + 1;
                    else cand = 0;

                    // 충돌 회피
                    while (used.Contains(cand)) cand++;

                    if (cand < 0 || cand > 255)
                        throw new FormatException("SubIdx auto-assignment overflow (>255).");

                    used.Add(cand);
                    lastAssigned = cand;
                    return (byte)cand;
                }

                // 실제 파싱부
                foreach (var subItem in SubItems)
                {
                    var SubIndex = subItem.Element(ns + "SubIdx");
                    var SubIndexName = subItem.Element(ns + "Name");
                    var SubBitSize = subItem.Element(ns + "BitSize");
                    var SubBitOffSet = subItem.Element(ns + "BitOffs");
                    var SubType = subItem.Element(ns + "Type");
                    var SubFlagsElem = subItem.Element(ns + "Flags");

                    var subFlags = new Flags();
                    if (SubFlagsElem != null)
                        subFlags = ParseFlags(SubFlagsElem, ns);

                    var sub = new ESISubDataType();

                    
                    byte assignedIdx = (byte)(SubIndex != null ? ParseUint(SubIndex.Value) : SubIndexNothing()); //기존 null이면 0 처리로 문제발생.(Fastech 드라이브에서)
                    sub.SubIndex = assignedIdx;

                    // lastAssigned를 "명시값"에도 반영
                    if (SubIndex != null)
                    {
                        int explicitIdx = (int)ParseUint(SubIndex.Value);
                        if (!used.Contains(explicitIdx)) used.Add(explicitIdx);
                        lastAssigned = explicitIdx;
                    }

                    sub.Name = SubIndexName != null ? SubIndexName.Value : "";
                    sub.BitSize = (ushort)(SubBitSize != null ? ParseUint(SubBitSize.Value) : 0);
                    sub.BitOffs = (ushort)(SubBitOffSet != null ? ParseUint(SubBitOffSet.Value) : 0);
                    sub.Type = SubType != null ? SubType.Value : "";
                    sub.Flag = subFlags;

                    SubItemTypes.Add(sub);
                }

                var DeviceDataType = new ESIDataType();
                DeviceDataType.Name = Name != null ? Name.Value : "";
                DeviceDataType.BitSize = (ushort)(BitSize != null ? ParseUint(BitSize.Value) : 0);
                DeviceDataType.BaseType = BaseType != null ? BaseType.Value : "";
                DeviceDataType.SubType = SubItemTypes;

                ret.Add(DeviceDataType);
            }

            return ret;
        }


        private static Flags ParseFlags(XElement FlagsElement, XNamespace ns)
        {
            var FlagsAccess = FlagsElement.Element("Access");

            var WriteRestrictions = FlagsAccess?.Attribute("WriteRestrictions");

            var FlagsCategory = FlagsElement.Element("Category");
            var FlagsPdoMapping = FlagsElement.Element("PdoMapping");

            Flags flags = new Flags
            {
                Access = (string)FlagsAccess ?? "",
                WriteRestrictions = (string)WriteRestrictions ?? "",
                Category = (string)FlagsCategory ?? "",
                PDOMapping = (string)FlagsPdoMapping ?? ""
            };

            return flags;
        }



        private static int ParseProfileNo(XElement profileNoElem, XNamespace ns)
        {
            var ProfilenoElement = profileNoElem.Element(ns + "ProfileNo");

            if (ProfilenoElement != null)
                throw new Exception("ProfileNo missing Number attribute");
            return (int)ParseUint(ProfilenoElement.Value);
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

                sm.MinSize = (ushort)ParseUint(minSizeAttr?.Value ?? "0");
                sm.MaxSize = (ushort)ParseUint(maxSizeAttr?.Value ?? "0");
                sm.DefaultSize = (ushort)ParseUint(defaultSizeAttr?.Value ?? "0");
                sm.StartAddress = (ushort)ParseUint(startAddrAttr.Value);
                sm.ControlByte = (ushort)ParseUint(controlAttr?.Value ?? "0");
                sm.Enable = (ushort)ParseUint(enableAttr?.Value ?? "0");

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
                    BitLength = (byte)ParseUint(eBits.Value),
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
            {
                s = s.Substring(2);
                return uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(2);
                return uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

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
