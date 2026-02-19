using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace SOEM_FrontEnd.Util
{
    public static class PcapIfEnumerator
    {
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pcap_findalldevs(ref nint alldevs, StringBuilder errbuf);

        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void pcap_freealldevs(nint alldevs);

        [StructLayout(LayoutKind.Sequential)]
        private struct pcap_if
        {
            public nint next;        // struct pcap_if* next
            public nint name;        // char*
            public nint description; // char*
            public nint addresses;   // struct pcap_addr* (무시)
            public uint flags;
        }

        public static IEnumerable<(string ifname, string description)> GetAll()
        {
            var list = new List<(string, string)>();
            var err = new StringBuilder(256);
            nint alldevs = nint.Zero;

            int rc = pcap_findalldevs(ref alldevs, err);
            if (rc != 0 || alldevs == nint.Zero)
                throw new InvalidOperationException($"pcap_findalldevs failed: {err}");

            try
            {
                nint cur = alldevs;
                while (cur != nint.Zero)
                {
                    var dev = Marshal.PtrToStructure<pcap_if>(cur);
                    string name = Marshal.PtrToStringAnsi(dev.name) ?? "";
                    string desc = Marshal.PtrToStringAnsi(dev.description) ?? "";
                    list.Add((name, desc));
                    cur = dev.next;
                }
            }
            finally
            {
                pcap_freealldevs(alldevs);
            }
            return list;
        }


    }
}
