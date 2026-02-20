using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Util.Logging
{
    // UI/콘솔/원격 등 "한 줄 로그"를 받을 수 있는 싱크 인터페이스
    public interface IOPLogUiSink
    {
        void Enqueue(string line);
    }

    public sealed class NullUiSink : IOPLogUiSink
    {
        public static readonly NullUiSink Instance = new NullUiSink();
        private NullUiSink() { }
        public void Enqueue(string line) { }
    }

}
