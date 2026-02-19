using System;
using System.Runtime.InteropServices;

namespace SOEM_FrontEnd.Util
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
        private static extern nint AvSetMmThreadCharacteristicsW(string taskName, out uint taskIndex);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvSetMmThreadPriority(nint avrtHandle, AvrtPriority priority);

        [DllImport("avrt.dll", SetLastError = true)]
        private static extern bool AvRevertMmThreadCharacteristics(nint avrtHandle);

        /// <summary>
        /// 현재 스레드를 MMCSS "Pro Audio" 태스크로 올리고 CRITICAL 우선순위로 설정.
        /// 실패하면 IntPtr.Zero 반환.
        /// </summary>
        public static nint EnterProAudio(out int lastError)
        {
            lastError = 0;

            uint idx;

            nint handle = AvSetMmThreadCharacteristicsW("Pro Audio", out idx);
            if (handle == nint.Zero)
            {
                lastError = Marshal.GetLastWin32Error();
                return nint.Zero;
            }

            if (!AvSetMmThreadPriority(handle, AvrtPriority.AVRT_PRIORITY_CRITICAL))
            {
                lastError = Marshal.GetLastWin32Error();
                // 여기서 revert 하고 0을 리턴할지, handle은 유지할지 정책 선택
                // 보수적으로는 revert 후 실패 처리 권장
                AvRevertMmThreadCharacteristics(handle);
                return nint.Zero;
            }

            return handle;
        }

        public static void LeaveMmcss(nint handle)
        {
            if (handle != nint.Zero)
            {
                AvRevertMmThreadCharacteristics(handle);
            }
        }
    }
}
