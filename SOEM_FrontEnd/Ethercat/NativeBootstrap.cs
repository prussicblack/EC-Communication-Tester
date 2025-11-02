using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat
{
    internal static class NativeBootstrap
    {
        static NativeBootstrap()
        {
            // 이 어셈블리에 대한 DllImport 해석기를 등록
            NativeLibrary.SetDllImportResolver(typeof(NativeBootstrap).Assembly, Resolve);

            // (선택) Npcap 런타임 DLL 선로딩: PATH 문제 회피
            TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "Npcap", "wpcap.dll"));
            TryLoad(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "Npcap", "Packet.dll"));
        }

        public static void EnsureLoaded() { /* 정적 생성자 트리거용 */ }

        private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? _)
        {
            if (!libraryName.Equals("soem_wrap", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero; // 다른 네이티브는 기본 규칙대로

            // 1순위: 실행 폴더 (배포 시 일반적)
            string baseDir = AppContext.BaseDirectory;
            string cand = Path.Combine(baseDir, "soem_wrap.dll");
            if (File.Exists(cand) && NativeLibrary.TryLoad(cand, out var h1))
                return h1;

            // 2순위: 개발 환경(Debug 출력 폴더 등)
            string dev = @"C:\Users\ursae\Desktop\Git\SOEM\build\x64\Debug\soem_wrap.dll"; // ← 본인 경로
            if (File.Exists(dev) && NativeLibrary.TryLoad(dev, out var h2))
                return h2;

            // 3순위: RID 폴더 구조 사용 시
            string rid = Path.Combine(baseDir, "runtimes", "win-x64", "native", "soem_wrap.dll");
            if (File.Exists(rid) && NativeLibrary.TryLoad(rid, out var h3))
                return h3;

            return IntPtr.Zero; // 못 찾으면 기본 로더가 진행 → DllNotFoundException
        }

        private static void TryLoad(string path)
        {
            if (File.Exists(path))
                NativeLibrary.TryLoad(path, out _);
        }
    }
}
