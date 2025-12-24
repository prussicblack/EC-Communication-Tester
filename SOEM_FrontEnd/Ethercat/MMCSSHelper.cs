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
        public static IntPtr EnterProAudio(out int lastError)
        {
            lastError = 0;

            uint idx;

            IntPtr handle = AvSetMmThreadCharacteristicsW("Pro Audio", out idx);
            if (handle == IntPtr.Zero)
            {
                lastError = Marshal.GetLastWin32Error();
                return IntPtr.Zero;
            }

            if (!AvSetMmThreadPriority(handle, AvrtPriority.AVRT_PRIORITY_CRITICAL))
            {
                lastError = Marshal.GetLastWin32Error();
                // 여기서 revert 하고 0을 리턴할지, handle은 유지할지 정책 선택
                // 보수적으로는 revert 후 실패 처리 권장
                AvRevertMmThreadCharacteristics(handle);
                return IntPtr.Zero;
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
