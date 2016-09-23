using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Library.Messaging
{
    public class EventQueue<T> : ManagerBase
    {
        private Dictionary<Delegate, EventItem> _events = new Dictionary<Delegate, EventItem>();

        private readonly object _thisLock = new object();
        private volatile bool _disposed;

        public void Enqueue(T item)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_thisLock)
            {
                foreach (var eventItem in _events.Values)
                {
                    eventItem.Enqueue(item);
                }
            }
        }

        public void Enqueue(IEnumerable<T> items)
        {
            if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

            lock (_thisLock)
            {
                foreach (var eventItem in _events.Values)
                {
                    eventItem.Enqueue(items);
                }
            }
        }

        public event Action<IEnumerable<T>> Events
        {
            add
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (_thisLock)
                {
                    _events.Add(value, new EventItem(value));
                }
            }
            remove
            {
                if (_disposed) throw new ObjectDisposedException(this.GetType().FullName);

                lock (_thisLock)
                {
                    EventItem item;
                    if (!_events.TryGetValue(value, out item)) return;

                    item.Dispose();
                    _events.Remove(value);
                }
            }
        }

        class EventItem : ManagerBase
        {
            private Action<IEnumerable<T>> _action;
            private Thread _thread;

            private List<T> _queue = new List<T>();
            private volatile ManualResetEvent _resetEvent = new ManualResetEvent(false);
            private readonly object _thisLock = new object();

            private volatile bool _disposed;

            public EventItem(Action<IEnumerable<T>> action)
            {
                _action = action;

                _thread = new Thread(this.WatchThread);
                _thread.Priority = ThreadPriority.BelowNormal;
                _thread.Name = "EventQueue_WatchThread";

                _thread.Start();
            }

            private void WatchThread()
            {
                try
                {
                    for (;;)
                    {
                        T[] result;

                        for (;;)
                        {
                            if (_disposed) return;

                            lock (_thisLock)
                            {
                                if (_queue.Count > 0)
                                {
                                    result = _queue.ToArray();
                                    _queue.Clear();

                                    break;
                                }
                                else
                                {
                                    _resetEvent.Reset();
                                }
                            }

                            _resetEvent.WaitOne();
                        }

                        try
                        {
                            _action(result);
                        }
                        catch (Exception e)
                        {
                            Log.Error(e);
                        }
                    }
                }
                catch (Exception)
                {

                }
            }

            public void Enqueue(T item)
            {
                lock (_thisLock)
                {
                    _queue.Add(item);
                    _resetEvent.Set();
                }
            }

            public void Enqueue(IEnumerable<T> items)
            {
                lock (_thisLock)
                {
                    _queue.AddRange(items);
                    _resetEvent.Set();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (_disposed) return;
                _disposed = true;

                if (disposing)
                {
                    if (_resetEvent != null)
                    {
                        try
                        {
                            _resetEvent.Set();
                            _resetEvent.Dispose();
                        }
                        catch (Exception)
                        {

                        }

                        _resetEvent = null;
                    }

                    _thread.Join();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
                foreach (var eventItem in _events.Values)
                {
                    eventItem.Dispose();
                }

                _events.Clear();
            }
        }
    }
}
