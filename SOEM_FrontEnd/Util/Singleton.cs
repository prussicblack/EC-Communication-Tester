using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Util
{
    public abstract class Singleton<T> where T : class, new()
    {
        protected Singleton() { }

        private static readonly Lazy<T> instance = new Lazy<T>(() => new T());

        public static T Instance
        {
            get
            {
                return instance.Value;
            }
        }
    }
}
