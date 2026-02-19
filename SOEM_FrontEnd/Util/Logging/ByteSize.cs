using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Util.Logging
{
    internal static class ByteSize
    {
        public static long Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string s = text.Trim().ToUpperInvariant();
            long mul = 1;

            if (s.EndsWith("KB"))
            {
                mul = 1024L;
                s = s.Substring(0, s.Length - 2).Trim();
            }
            else if (s.EndsWith("MB"))
            {
                mul = 1024L * 1024L;
                s = s.Substring(0, s.Length - 2).Trim();
            }
            else if (s.EndsWith("GB"))
            {
                mul = 1024L * 1024L * 1024L;
                s = s.Substring(0, s.Length - 2).Trim();
            }
            else if (s.EndsWith("TB"))
            {
                mul = 1024L * 1024L * 1024L * 1024L;
                s = s.Substring(0, s.Length - 2).Trim();
            }

            long n;
            if (!long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return 0;
            if (n <= 0)
                return 0;

            return n * mul;
        }
    }
}
