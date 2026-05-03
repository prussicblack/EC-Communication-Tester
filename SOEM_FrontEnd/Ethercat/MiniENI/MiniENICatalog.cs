using Microsoft.Extensions.Logging;
using SOEM_FrontEnd.Util.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOEM_FrontEnd.Ethercat.MiniENI
{
    public static class MiniENICatalog
    {
        private static readonly object _lock = new object();

        private static string _baseDirectory = "";
        private static string _defaultFileName = "eni.json";

        private static MiniENI _current;


        public static MiniENI Current
        {
            get
            {
                lock (_lock)
                {
                    return _current;
                }
            }
        }

        public static string BaseDirectory
        {
            get
            {
                lock (_lock)
                {
                    return _baseDirectory;
                }
            }
        }

        public static string DefaultFileName
        {
            get
            {
                lock (_lock)
                {
                    return _defaultFileName;
                }
            }
        }

        public static bool HasCurrent
        {
            get
            {
                lock (_lock)
                {
                    return _current != null;
                }
            }
        }

        private static ILogger log;

        public static void Initialize(string baseDirectory)
        {
            if (log == null)
            {
                log = OPLogger.CreateLogger("App");
            }

            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MiniENI");
            }

            lock (_lock)
            {
                _baseDirectory = baseDirectory;

                if (Directory.Exists(_baseDirectory) == false)
                {
                    Directory.CreateDirectory(_baseDirectory);
                }
            }
        }

        public static void Initialize(string baseDirectory, string defaultFileName)
        {
            if (log == null)
            {
                log = OPLogger.CreateLogger("App");
            }

            if (string.IsNullOrWhiteSpace(defaultFileName))
            {
                defaultFileName = "eni.json";
            }

            Initialize(baseDirectory);

            lock (_lock)
            {
                _defaultFileName = defaultFileName;
            }
        }

        public static bool TryLoadDefault(out MiniENI eni, out string message)
        {
            //string path = GetDefaultPath();
            return TryLoad(_defaultFileName, out eni, out message);
        }

        public static MiniENI LoadDefault()
        {
            //string path = GetDefaultPath();
            return Load(_defaultFileName);
        }

        public static bool TryLoad(string filename, out MiniENI eni, out string message)
        {
            eni = null;
            message = "";

            try
            {
                string path = $@"{_baseDirectory}\{filename}";

                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "MiniENI path is empty.";
                    log.LogWarning(message);
                    return false;
                }

                if (File.Exists(path) == false)
                {
                    message = "MiniENI file does not exist: " + path;
                    log.LogWarning(message);
                    return false;
                }

                string json = File.ReadAllText(path);
                MiniENI loaded = MiniENIJson.Deserialize(json);

                if (loaded == null)
                {
                    message = "MiniENI deserialize failed: " + path;
                    log.LogWarning(message);
                    return false;
                }

                if (loaded.Adapter == null)
                {
                    loaded.Adapter = new EniAdapterConfig();
                }

                if (loaded.Slaves == null)
                {
                    loaded.Slaves = new System.Collections.Generic.List<EniSlaveConfig>();
                }

                lock (_lock)
                {
                    _current = loaded;
                }

                eni = loaded;
                message = "MiniENI loaded: " + path;
                log.LogInformation(message);

                return true;
            }
            catch (Exception ex)
            {
                message = "MiniENI load failed: " + ex.Message;
                log.LogError(message);

                return false;
            }
        }

        public static MiniENI Load(string filename)
        {
            MiniENI project;
            string message;

            bool ok = TryLoad(filename, out project, out message);

            if (ok == false)
            {
                throw new InvalidOperationException(message);
            }

            return project;
        }

        public static bool TrySaveDefault(MiniENI eni, out string message)
        {
            //string path = GetDefaultPath();
            return TrySave(_defaultFileName, eni, out message);
        }

        public static void SaveDefault(MiniENI project)
        {
            //string path = GetDefaultPath();
            Save(_defaultFileName, project);
        }

        public static bool TrySave(string filename, MiniENI eni, out string message)
        {
            message = "";

            try
            {
                string path = $@"{_baseDirectory}\{filename}";

                if (eni == null)
                {
                    message = "MiniENI Data is null.";
                    log.LogWarning(message);

                    return false;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    message = "MiniENI path is empty.";
                    log.LogWarning(message);

                    return false;
                }

                //string directory = Path.GetDirectoryName(path);

                if (string.IsNullOrWhiteSpace(_baseDirectory) == false &&
                    Directory.Exists(_baseDirectory) == false)
                {
                    Directory.CreateDirectory(_baseDirectory);
                }

                MiniENIJson.Normalize(eni);

                string json = MiniENIJson.Serialize(eni);
                File.WriteAllText(path, json);

                lock (_lock)
                {
                    _current = eni;
                }

                message = "MiniENI saved: " + path;
                log.LogInformation(message);

                return true;
            }
            catch (Exception ex)
            {
                message = "MiniENI save failed: " + ex.Message;
                log.LogError(message);

                return false;
            }
        }

        public static void Save(string filename, MiniENI eni)
        {
            string message;
            bool ok = TrySave(filename, eni, out message);

            if (ok == false)
            {
                throw new InvalidOperationException(message);
            }
        }

        public static string GetDefaultPath()
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(_baseDirectory))
                {
                    _baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MiniENI");
                }

                if (Directory.Exists(_baseDirectory) == false)
                {
                    Directory.CreateDirectory(_baseDirectory);
                }

                return Path.Combine(_baseDirectory, _defaultFileName);
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _current = null;
            }
        }
    }
}
