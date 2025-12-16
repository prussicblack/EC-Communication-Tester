using System;
using System.Runtime.InteropServices;

namespace SOEM_FrontEnd.Ethercat
{
    internal static class MMCSSHelper
    {
        private enum AvrtPriority
        {
            AVRT_PRIORITY_LOW = -1,
            AVRT_PRIORITY_NORMAL = 0,
            AVRT_PRIORITY_HIGH = 1,
            AVRT_PRIORITY_CRITICAL = 2
        }

        [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr AvSetMmThreadCharacteristicsW(string taskName, out uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvSetMmThreadPriority(IntPtr avrtHandle, AvrtPriority priority);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);

        /// <summary>
        /// 현재 스레드를 MMCSS "Pro Audio" 태스크로 올리고 CRITICAL 우선순위로 설정.
        /// 실패하면 IntPtr.Zero 반환.
        /// </summary>
        public static IntPtr EnterProAudio()
        {
            uint idx;
            IntPtr handle = AvSetMmThreadCharacteristicsW("Pro Audio", out idx);
            if (handle != IntPtr.Zero)
            {
                // 실패해도 크게 문제는 아니니, 에러는 굳이 throw 하지 않음
                AvSetMmThreadPriority(handle, AvrtPriority.AVRT_PRIORITY_CRITICAL);
            }
            return handle;
        }

        public static void LeaveMmcss(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                AvRevertMmThreadCharacteristics(handle);
            }
        }
    }
}
