using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using static SOEM_FrontEnd.Ethercat.ESI.ESIXMLData;

namespace SOEM_FrontEnd.Ethercat.ESI
{
    public sealed class ESICatalog
    {
        private readonly Dictionary<Tuple<uint, uint, uint>, ESIXMLData.EsiDevice> _byKey =
            new Dictionary<Tuple<uint, uint, uint>, ESIXMLData.EsiDevice>();

        public IReadOnlyDictionary<Tuple<uint, uint, uint>, ESIXMLData.EsiDevice> DevicesByKey => _byKey;

        public static ESICatalog LoadFolder(string folder)
        {
            var cat = new ESICatalog();

            //var snixmls = SearchSubfolderESIXML(folder);

            //foreach (var path in snixmls)
            foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory + folder, "*.xml",
                         SearchOption.AllDirectories))
            {
                try
                {
                    var list = ParseDevicesFromFile(path);
                    foreach (var d in list)
                    {
                        var key = Tuple.Create(d.VendorId, d.ProductCode, d.RevisionNo);
                        cat._byKey[key] = d; // 인스턴스 필드에 채움 (중요: cat 통해 접근)
                    }
                }
                catch
                {
                    /* 로깅 후 계속 */
                }
            }

            return cat;
        }

        public bool TryGetDevice(uint v, uint p, uint r, out EsiDevice dev)
        {
            return _byKey.TryGetValue(Tuple.Create(v, p, r), out dev);
        }

        //Scan XML Sni.
        private static List<string> SearchSubfolderESIXML(string path, ISet<string> skipDirs = null)
        {
            string currentpath = AppContext.BaseDirectory;
            string ESIPath = currentpath + path;

            List<string> ret = new List<string>();

            Stack<string> dirs = new Stack<string>();
            dirs.Push(ESIPath);

            if (skipDirs == null)
                skipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs" };


            while (dirs.Count > 0)
            {
                string dir = dirs.Pop();
                string name = Path.GetFileName(dir);
                if (skipDirs.Contains(name)) continue;

                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir, "*.xml"))
                        ret.Add(f);
                }
                catch
                {
                    /* 접근 불가 폴더/파일 무시 */
                }

                try
                {
                    foreach (var sd in Directory.EnumerateDirectories(dir))
                        dirs.Push(sd);
                }
                catch
                {
                    /* 접근 불가 폴더 무시 */
                }
            }

            return ret;

        }

        // (선택) Revision 범위/마스크 매칭이 필요하면 여기서 베스트 매치 로직 추가
        // public bool TryGetBestMatch(uint vendor, uint product, uint revision, out EsiDevice device) { ... }

        //data type이 object에 들어가있네..--;;
        //외부에 노툴되는 Load, Tryget에 Summery 적용할것, Tryget에 인자이름 줄여쓰지마라.


        // ---------- 파일 파싱: 파일 내 모든 <Device> 수집 ----------
        private static List<EsiDevice> ParseDevicesFromFile(string path)
        {
            var result = new List<EsiDevice>();

            var xrSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true,
                XmlResolver = null
            };

            using (var fs = File.OpenRead(path))
            using (var xr = XmlReader.Create(fs, xrSettings))
            {
                var doc = XDocument.Load(xr, LoadOptions.None);
                var root = doc.Root;
                if (root == null) return result;

                // 1) 파일 전역 VendorId (있으면 사용)
                uint vendorId = 0;
                var xVendor = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "Vendor");
                if (xVendor != null)
                {
                    var xVid = xVendor.Descendants().FirstOrDefault(e => e.Name.LocalName == "Id");
                    if (xVid != null)
                    {
                        uint tmpVid;
                        if (TryParseUIntFlexible(xVid.Value.Trim(), out tmpVid))
                            vendorId = tmpVid;
                    }
                }

                // 2) 모든 <Device> 처리
                var devices = root.Descendants().Where(e => e.Name.LocalName == "Device");
                foreach (var xDev in devices)
                {
                    // (a) 타입/키: <Type ProductCode="…" RevisionNo="…"> 또는 내부 텍스트/자식
                    var xType = xDev.Elements().FirstOrDefault(e => e.Name.LocalName == "Type");
                    if (xType == null) continue;

                    uint prod = 0, rev = 0, vidLocal = vendorId;

                    // VendorId가 <Type VendorId="…"> 속성에 있는 경우도 가끔 있음 (보조 처리)
                    var aVendor = xType.Attributes().FirstOrDefault(a => a.Name.LocalName == "VendorId");
                    if (aVendor != null)
                    {
                        uint tmp;
                        if (TryParseUIntFlexible(aVendor.Value.Trim(), out tmp)) vidLocal = tmp;
                    }

                    // ProductCode/RevisionNo는 속성으로 오는 경우가 흔함 (#x / 0x / 10진)
                    var aProd = xType.Attributes().FirstOrDefault(a => a.Name.LocalName == "ProductCode");
                    var aRev = xType.Attributes().FirstOrDefault(a => a.Name.LocalName == "RevisionNo");
                    if (aProd != null) TryParseUIntFlexible(aProd.Value.Trim(), out prod);
                    if (aRev != null) TryParseUIntFlexible(aRev.Value.Trim(), out rev);

                    // 속성에 없고, 자식/텍스트로 제공되는 벤더도 대비 (보조)
                    if (vidLocal == 0)
                    {
                        var xVendorIdLegacy = Child(xType, "VendorId");
                        if (xVendorIdLegacy != null)
                        {
                            uint tmp;
                            if (TryParseUIntFlexible(xVendorIdLegacy.Value.Trim(), out tmp)) vidLocal = tmp;
                        }
                    }

                    if (prod == 0)
                    {
                        var xProdLegacy = Child(xType, "ProductCode");
                        if (xProdLegacy != null)
                        {
                            uint tmp;
                            if (TryParseUIntFlexible(xProdLegacy.Value.Trim(), out tmp)) prod = tmp;
                        }
                    }

                    if (rev == 0)
                    {
                        var xRevLegacy = Child(xType, "RevisionNo");
                        if (xRevLegacy != null)
                        {
                            // 값이 없고 Min/Max/Mask 속성만 있는 경우가 있음 → 우선 Min을 사용(정책은 상황에 맞게 바꿔도 됨)
                            uint tmp;
                            if (TryParseUIntFlexible(xRevLegacy.Value.Trim(), out tmp))
                            {
                                rev = tmp;
                            }
                            else
                            {
                                var sMin = Attr(xRevLegacy, "Min");
                                if (!string.IsNullOrEmpty(sMin))
                                {
                                    if (TryParseUIntFlexible(sMin, out tmp)) rev = tmp;
                                }
                                else
                                {
                                    var sMask = Attr(xRevLegacy, "Mask"); // 필요시 마스크 매칭 정책 구현
                                    // 여기서는 별도 처리 안 함
                                }
                            }
                        }
                    }

                    // 최소한의 키가 없으면 스킵
                    if (vidLocal == 0 || prod == 0 || rev == 0)
                        continue;

                    var dev = new EsiDevice();
                    dev.VendorId = vidLocal;
                    dev.ProductCode = prod;
                    dev.RevisionNo = rev;

                    // Name: <Name> 또는 <Type>의 텍스트
                    var xName = xDev.Elements().FirstOrDefault(e => e.Name.LocalName == "Name");
                    dev.Name = xName != null ? (xName.Value ?? "").Trim() : (xType.Value ?? "").Trim();

                    // 3) Profile / CoE / 기타 지원 프로토콜 플래그
                    dev.Coe = new CoeProfile();
                    var xProfile = xDev.Elements().FirstOrDefault(e => e.Name.LocalName == "Profile");
                    if (xProfile != null)
                    {
                        var xCoE = Child(xProfile, "CoE");
                        if (xCoE != null)
                        {
                            dev.Coe.Enabled = true;
                            dev.Coe.SdoInfo = ReadBool(Child(xCoE, "SdoInfo"));
                            dev.Coe.PdoAssign = ReadBool(Child(xCoE, "PdoAssign"));
                            dev.Coe.PdoConfig = ReadBool(Child(xCoE, "PdoConfig"));
                            dev.Coe.PdoUpload = ReadBool(Child(xCoE, "PdoUpload"));
                        }
                        // 필요 시 FoE/EoE/SoE 존재 여부 저장하려면 xProfile 내부에서 Child(...,"FoE") 등 체크
                    }

                    // 4) DC
                    dev.Dc = new DcInfo();
                    var xDc = xDev.Elements().FirstOrDefault(e => e.Name.LocalName == "Dc");
                    if (xDc != null)
                    {
                        dev.Dc.Supported = true;
                        dev.Dc.CycleTime0Ns = ReadLong(Child(xDc, "CycleTime0"));
                        dev.Dc.CycleTime1Ns = ReadLong(Child(xDc, "CycleTime1"));
                        dev.Dc.ShiftNs = ReadLong(Child(xDc, "Shift"));
                        dev.Dc.AssignActivate = ReadUShort(Child(xDc, "AssignActivate"));
                    }

                    // 5) PDO (Rx/Tx)
                    foreach (var xp in xDev.Descendants().Where(e => e.Name.LocalName == "RxPdo"))
                        dev.RxPdos.Add(ParsePdo(xp));
                    foreach (var xp in xDev.Descendants().Where(e => e.Name.LocalName == "TxPdo"))
                        dev.TxPdos.Add(ParsePdo(xp));

                    // 6) Dictionary / Objects
                    var xDict = xDev.Descendants().FirstOrDefault(e => e.Name.LocalName == "Dictionary");
                    if (xDict != null)
                    {
                        foreach (var xo in xDict.Descendants().Where(e => e.Name.LocalName == "Object"))
                            dev.Objects.Add(ParseObject(xo));
                    }

                    result.Add(dev);
                }
            }

            return result;
        }

        private static XElement Child(XElement parent, string localName)
        {
            if (parent == null) return null;
            foreach (var e in parent.Elements())
                if (e.Name.LocalName == localName)
                    return e;
            return null;
        }

        // 네임스페이스 무시 속성값 가져오기
        private static string Attr(XElement elem, string attrName)
        {
            if (elem == null) return null;
            foreach (var a in elem.Attributes())
                if (a.Name.LocalName == attrName)
                    return a.Value;
            return null;
        }

        // #x / 0x / 10진 모두 허용
        private static bool TryParseUIntFlexible(string s, out uint v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s.StartsWith("#x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
            return uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        private static ushort? ReadUShort(XElement x)
        {
            if (x == null) return null;
            ushort v;
            var s = x.Value.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return ushort.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
                    ? (ushort?)v
                    : null;
            return ushort.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? (ushort?)v : null;
        }

        private static long? ReadLong(XElement x)
        {
            if (x == null) return null;
            long v;
            return long.TryParse(x.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                ? (long?)v
                : null;
        }

        private static bool ReadBool(XElement x)
        {
            if (x == null) return false;
            var s = x.Value.Trim();
            if (s == "1") return true;
            if (s == "0") return false;
            bool v;
            return bool.TryParse(s, out v) && v;
        }

        // PDO/Object 파서는 이전에 쓰던 걸 그대로 사용
        private static Pdo ParsePdo(XElement xPdo)
        {
            var p = new Pdo();
            p.Index = ReadUShort(Child(xPdo, "Index")) ?? (ushort)0;
            p.Name = (Child(xPdo, "Name") != null ? Child(xPdo, "Name").Value : "").Trim();
            p.Default = ReadBool(Child(xPdo, "Default"));

            foreach (var xe in xPdo.Elements().Where(e => e.Name.LocalName == "Entry"))
            {
                var e = new PdoEntry();
                e.Index = ReadUShort(Child(xe, "Index")) ?? (ushort)0;

                // 일부 ESI는 SubIndex를 byte 대신 ushort로 쓰기도 → 보수적으로 파싱
                var sub = ReadUShort(Child(xe, "SubIndex"));
                e.SubIndex = (byte)(sub.HasValue ? sub.Value : 0);

                e.BitLen = (int)(ReadLong(Child(xe, "BitLen")) ?? 0);
                e.Name = (Child(xe, "Name") != null ? Child(xe, "Name").Value : "").Trim();
                p.Entries.Add(e);
            }

            return p;
        }

        private static EcatObject ParseObject(XElement xo)
        {
            var o = new EcatObject();
            o.Index = ReadUShort(Child(xo, "Index")) ?? (ushort)0;
            o.Name = (Child(xo, "Name") != null ? Child(xo, "Name").Value : "").Trim();
            o.DataType = (Child(xo, "DataType") != null ? Child(xo, "DataType").Value : "").Trim();
            o.Access = (Child(xo, "Access") != null ? Child(xo, "Access").Value : "").Trim();
            o.Default = (Child(xo, "Default") != null ? Child(xo, "Default").Value : "").Trim();
            var xm = Child(xo, "PdoMapping");
            if (xm != null) o.PdoMapping = ReadBool(xm);

            foreach (var xs in xo.Elements().Where(e => e.Name.LocalName == "SubItem"))
            {
                var s = new EcatSubItem();
                var sub = ReadUShort(Child(xs, "SubIndex"));
                s.SubIndex = (byte)(sub.HasValue ? sub.Value : 0);
                s.Name = (Child(xs, "Name") != null ? Child(xs, "Name").Value : "").Trim();
                s.DataType = (Child(xs, "DataType") != null ? Child(xs, "DataType").Value : "").Trim();
                s.Access = (Child(xs, "Access") != null ? Child(xs, "Access").Value : "").Trim();
                s.Default = (Child(xs, "Default") != null ? Child(xs, "Default").Value : "").Trim();
                var sm = Child(xs, "PdoMapping");
                if (sm != null) s.PdoMapping = ReadBool(sm);
                o.Subs.Add(s);
            }

            return o;


        }
    }
}
