using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace SOEM_FrontEnd.Ethercat
{
    public static class PcapIfEnumerator
    {
        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int pcap_findalldevs(ref IntPtr alldevs, StringBuilder errbuf);

        [DllImport("wpcap.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void pcap_freealldevs(IntPtr alldevs);

        [StructLayout(LayoutKind.Sequential)]
        private struct pcap_if
        {
            public IntPtr next;        // struct pcap_if* next
            public IntPtr name;        // char*
            public IntPtr description; // char*
            public IntPtr addresses;   // struct pcap_addr* (무시)
            public uint flags;
        }

        public static IEnumerable<(string ifname, string description)> GetAll()
        {
            var list = new List<(string, string)>();
            var err = new StringBuilder(256);
            IntPtr alldevs = IntPtr.Zero;

            int rc = pcap_findalldevs(ref alldevs, err);
            if (rc != 0 || alldevs == IntPtr.Zero)
                throw new InvalidOperationException($"pcap_findalldevs failed: {err}");

            try
            {
                IntPtr cur = alldevs;
                while (cur != IntPtr.Zero)
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
