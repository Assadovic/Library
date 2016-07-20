using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Library.Net.Covenant
{
    sealed class CountManager : IThisLock
    {
        private TimeSpan _survivalTime;

        private Dictionary<long, int> _table = new Dictionary<long, int>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public CountManager(TimeSpan survivalTime)
        {
            _survivalTime = survivalTime;
        }

        public TimeSpan SurvivalTime
        {
            get
            {
                return _survivalTime;
            }
        }

        public void Add(int count)
        {
            lock (this.ThisLock)
            {
                var key = (long)(DateTime.UtcNow - DateTime.MinValue).TotalSeconds;

                int origin;
                _table.TryGetValue(key, out origin);

                _table[key] = origin + count;
            }
        }

        public long Get()
        {
            lock (this.ThisLock)
            {
                return _table.Values.Sum(n => (long)n);
            }
        }

        public void Refresh()
        {
            lock (this.ThisLock)
            {
                var start = (long)(DateTime.UtcNow - DateTime.MinValue).TotalSeconds - _survivalTime.TotalMinutes;

                foreach (var key in _table.Keys.ToArray())
                {
                    if (key < start) _table.Remove(key);
                }
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
