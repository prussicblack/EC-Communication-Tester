using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SOEM_FrontEnd.Util.Logging;
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

    public static class ESICatalog
    {
        // 폴더 아래 모든 .xml 파일을 EsiFile로 읽기


        private static readonly object _lock = new object();

        private static bool _initialized;

        private static List<ESIDevice> _ESIDevice = new List<ESIDevice>();

        private static ILogger _log = NullLogger.Instance;


        public static void Initialize(string Path)
        {
            if (OPLogger.IsConfigured)
                _log = OPLogger.CreateLogger("SOEM_FrontEnd");
            else
                _log = NullLogger.Instance;

            _ESIDevice = LoadAllDevices(Path);
        }

        public static ESIDevice? GetDeviceData(uint Productcode, uint Vendorcode, uint Revision)
        {
            ESIDevice? ret = _ESIDevice.FirstOrDefault(d => d.ProductCode == Productcode && d.VendorId == Vendorcode && d.Revision == Revision);

            return ret;
        }



        public static List<EsiFile> LoadAllFromDirectory(string directoryPath)
        {
            var result = new List<EsiFile>();

            // 하위 폴더까지 다 보려면 AllDirectories, 현재 폴더만이면 TopDirectoryOnly
            var files = Directory.EnumerateFiles(directoryPath, "*.xml", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                try
                {
                    var esi = EsiParser.Parse(file);
                    result.Add(esi);
                }
                catch (Exception ex)
                {
                    // 필요하면 로그만 찍고 계속 진행
                    //Console.WriteLine($"ESI parse failed: {file} : {ex.Message}");
                    _log.LogInformation("Not initialized.");
                    _log.LogError(ex.ToString());
                }
            }

            return result;
        }

        // 모든 파일의 Device를 한 리스트로 평탄화해서 얻고 싶으면
        public static List<ESIDevice> LoadAllDevices(string directoryPath)
        {
            var files = LoadAllFromDirectory(directoryPath);
            var devices = new List<ESIDevice>();

            foreach (var f in files)
            {
                devices.AddRange(f.Devices);
            }

            return devices;
        }
    }
}
