using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Library.Collections;
using Library.Io;

namespace Library.Net.Covenant
{
    class ExchangeManager : StateManagerBase, Library.Configuration.ISettings, IThisLock
    {
        private string _workDirectory;
        private ConnectionsManager _connectionsManager;
        private BufferManager _bufferManager;

        private Settings _settings;

        private Thread _watchThread;
        private Dictionary<Seed, Thread> _exchangeThreads = new Dictionary<Seed, Thread>();

        private Dictionary<int, ExchangeItem> _ids = new Dictionary<int, ExchangeItem>();
        private int _id;

        private volatile ManagerState _state = ManagerState.Stop;

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        private int _threadCount = 2;

        public ExchangeManager(string workDirectory, ConnectionsManager connectionsManager, BufferManager bufferManager)
        {
            _workDirectory = workDirectory;
            _connectionsManager = connectionsManager;
            _bufferManager = bufferManager;

            _settings = new Settings(this.ThisLock);

            _threadCount = Math.Max(1, Math.Min(System.Environment.ProcessorCount, 32) / 2);
        }

        public Information Information
        {
            get
            {
                lock (this.ThisLock)
                {
                    var contexts = new List<InformationContext>();

                    contexts.Add(new InformationContext("ExchangingCount", _settings.ExchangeItems
                        .Count(n => !(n.State == ExchangeState.Completed || n.State == ExchangeState.Error))));

                    return new Information(contexts);
                }
            }
        }

        public IEnumerable<Information> ExchangingInformation
        {
            get
            {
                lock (this.ThisLock)
                {
                    var list = new List<Information>();

                    foreach (var item in _ids)
                    {
                        var contexts = new List<InformationContext>();

                        contexts.Add(new InformationContext("Id", item.Key));
                        contexts.Add(new InformationContext("Name", ExchangeManager.GetNormalizedPath(item.Value.Seed.Name ?? "")));
                        contexts.Add(new InformationContext("Length", item.Value.Seed.Length));
                        contexts.Add(new InformationContext("State", item.Value.State));
                        if (item.Value.Path != null) contexts.Add(new InformationContext("Path", Path.Combine(item.Value.Path, ExchangeManager.GetNormalizedPath(item.Value.Seed.Name ?? ""))));
                        else contexts.Add(new InformationContext("Path", ExchangeManager.GetNormalizedPath(item.Value.Seed.Name ?? "")));

                        contexts.Add(new InformationContext("Seed", item.Value.Seed));

                        if (item.Value.State == ExchangeState.ComputeHash)
                        {
                            contexts.Add(new InformationContext("StreamLength", item.Value.StreamLength));
                            contexts.Add(new InformationContext("StreamOffset", item.Value.StreamOffset));
                        }

                        list.Add(new Information(contexts));
                    }

                    return list;
                }
            }
        }

        public string BaseDirectory
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _settings.BaseDirectory;
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    _settings.BaseDirectory = value;
                }
            }
        }

        private static string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
            {
                return path;
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    return text;
                }
            }
        }

        private static FileStream GetUniqueFileStream(string path)
        {
            if (!File.Exists(path))
            {
                try
                {
                    return new FileStream(path, FileMode.CreateNew);
                }
                catch (DirectoryNotFoundException)
                {
                    throw;
                }
                catch (IOException)
                {

                }
            }

            for (int index = 1; ; index++)
            {
                string text = string.Format(@"{0}\{1} ({2}){3}",
                    Path.GetDirectoryName(path),
                    Path.GetFileNameWithoutExtension(path),
                    index,
                    Path.GetExtension(path));

                if (!File.Exists(text))
                {
                    try
                    {
                        return new FileStream(text, FileMode.CreateNew);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw;
                    }
                    catch (IOException)
                    {
                        if (index > 1024) throw;
                    }
                }
            }
        }

        private static string GetNormalizedPath(string path)
        {
            string filePath = path;

            foreach (char ic in Path.GetInvalidFileNameChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }
            foreach (char ic in Path.GetInvalidPathChars())
            {
                filePath = filePath.Replace(ic.ToString(), "-");
            }

            return filePath;
        }

        private void WatchThread()
        {
            for (;;)
            {
                Thread.Sleep(1000 * 1);
                if (this.State == ManagerState.Stop) return;

                try
                {
                    var items = _settings.ExchangeItems.Where(n => !_exchangeThreads.ContainsKey(n.Seed));

                    foreach (var item in items)
                    {
                        var thread = new Thread(this.ExchangeThread);
                        thread.Priority = ThreadPriority.BelowNormal;
                        thread.Name = "ExchangeManager_ExchangeThread";
                        thread.Start(item);

                        _exchangeThreads.Add(item.Seed, thread);
                    }

                }
                catch (Exception e)
                {
                    Log.Error(e);
                }
            }
        }

        private void ExchangeThread(object state)
        {
            try
            {
                var item = state as ExchangeItem;
                if (item == null) return;


            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public void Download(Seed seed, string path)
        {
            if (seed == null) return;

            lock (this.ThisLock)
            {
                if (_settings.ExchangeItems.Any(n => n.Seed == seed)) return;

                var item = new ExchangeItem();

                item.Type = ExchangeType.Download;
                item.State = ExchangeState.Exchanging;

                item.Seed = seed;
                item.Path = path;

                _settings.ExchangeItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Upload(string path)
        {
            if (path == null) return;

            lock (this.ThisLock)
            {
                if (_settings.ExchangeItems.Any(n => n.Path == path)) return;

                var item = new ExchangeItem();

                item.Type = ExchangeType.Upload;
                item.State = ExchangeState.ComputeHash;

                item.Path = path;

                _settings.ExchangeItems.Add(item);
                _ids.Add(_id++, item);
            }
        }

        public void Remove(int id)
        {
            lock (this.ThisLock)
            {
                var item = _ids[id];

                _settings.ExchangeItems.Remove(item);
                _ids.Remove(id);
            }
        }

        public override ManagerState State
        {
            get
            {
                return _state;
            }
        }

        private readonly object _stateLock = new object();

        public override void Start()
        {
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Start) return;
                    _state = ManagerState.Start;

                    _watchThread = new Thread(this.WatchThread);
                    _watchThread.Priority = ThreadPriority.BelowNormal;
                    _watchThread.Name = "ExchangeManager_WatchThread";
                    _watchThread.Start();
                }
            }
        }

        public override void Stop()
        {
            lock (_stateLock)
            {
                lock (this.ThisLock)
                {
                    if (this.State == ManagerState.Stop) return;
                    _state = ManagerState.Stop;
                }

                _watchThread.Join();
                _watchThread = null;
            }
        }

        #region ISettings

        public void Load(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Load(directoryPath);
            }
        }

        public void Save(string directoryPath)
        {
            lock (this.ThisLock)
            {
                _settings.Save(directoryPath);
            }
        }

        #endregion

        private class Settings : Library.Configuration.SettingsBase
        {
            private volatile object _thisLock;

            public Settings(object lockObject)
                : base(new List<Library.Configuration.ISettingContent>() {
                    new Library.Configuration.SettingContent<string>() { Name = "BaseDirectory", Value = "" },
                    new Library.Configuration.SettingContent<LockedList<ExchangeItem>>() { Name = "ExchangeItems", Value = new LockedList<ExchangeItem>() },
                })
            {
                _thisLock = lockObject;
            }

            public override void Load(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Load(directoryPath);
                }
            }

            public override void Save(string directoryPath)
            {
                lock (_thisLock)
                {
                    base.Save(directoryPath);
                }
            }

            public string BaseDirectory
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (string)this["BaseDirectory"];
                    }
                }
                set
                {
                    lock (_thisLock)
                    {
                        this["BaseDirectory"] = value;
                    }
                }
            }

            public LockedList<ExchangeItem> ExchangeItems
            {
                get
                {
                    lock (_thisLock)
                    {
                        return (LockedList<ExchangeItem>)this["ExchangeItems"];
                    }
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {

            }
        }

        #region IThisLock

        public object ThisLock
        {
            get
            {
                return _thisLock;
            }
        }

        #endregion
    }
}
