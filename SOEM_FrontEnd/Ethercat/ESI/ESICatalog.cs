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
        // 폴더 아래 모든 .xml 파일을 EsiFile로 읽기
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
                    Console.WriteLine($"ESI parse failed: {file} : {ex.Message}");
                }
            }

            return result;
        }

        // 모든 파일의 Device를 한 리스트로 평탄화해서 얻고 싶으면
        public static List<EsiDevice> LoadAllDevices(string directoryPath)
        {
            var files = LoadAllFromDirectory(directoryPath);
            var devices = new List<EsiDevice>();

            foreach (var f in files)
            {
                devices.AddRange(f.Devices);
            }

            return devices;
        }
    }
}
