﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Library.Collections
{
    public class VolatileDictionary<TKey, TValue> : IDictionary<TKey, TValue>, ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>, IDictionary, ICollection, IEnumerable, IThisLock
    {
        private Dictionary<TKey, TValue> _dic;
        private Dictionary<TKey, DateTime> _volatileDictionary;

        private DateTime _lastCheckTime = DateTime.MinValue;
        private readonly TimeSpan _survivalTime;

        private readonly object _thisLock = new object();

        public VolatileDictionary(TimeSpan survivalTime)
        {
            _dic = new Dictionary<TKey, TValue>();
            _volatileDictionary = new Dictionary<TKey, DateTime>();
            _survivalTime = survivalTime;
        }

        public VolatileDictionary(TimeSpan survivalTime, IEqualityComparer<TKey> comparer)
        {
            _dic = new Dictionary<TKey, TValue>(comparer);
            _volatileDictionary = new Dictionary<TKey, DateTime>(comparer);
            _survivalTime = survivalTime;
        }

        public TimeSpan SurvivalTime
        {
            get
            {
                return _survivalTime;
            }
        }

        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            lock (this.ThisLock)
            {
                var array = new KeyValuePair<TKey, TValue>[_dic.Count];
                ((IDictionary<TKey, TValue>)_dic).CopyTo(array, 0);

                return array;
            }
        }

        public void Refresh(TKey item)
        {
            this.CheckLifeTime();

            _volatileDictionary[item] = DateTime.UtcNow;
        }

        private void CheckLifeTime()
        {
            lock (this.ThisLock)
            {
                var now = DateTime.UtcNow;

                if ((now - _lastCheckTime).TotalSeconds >= 10)
                {
                    foreach (var item in _dic.ToArray())
                    {
                        if ((now - _volatileDictionary[item.Key]) > _survivalTime)
                        {
                            _dic.Remove(item.Key);
                        }
                    }

                    foreach (var item in _volatileDictionary.Keys.ToArray())
                    {
                        if (!_dic.ContainsKey(item))
                        {
                            _volatileDictionary.Remove(item);
                        }
                    }

                    _lastCheckTime = now;
                }
            }
        }

        public void TrimExcess()
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();
            }
        }

        public VolatileKeyCollection Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.CheckLifeTime();

                    return new VolatileKeyCollection(_dic.Keys, this.ThisLock);
                }
            }
        }

        public VolatileValueCollection Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.CheckLifeTime();

                    return new VolatileValueCollection(_dic.Values, this.ThisLock);
                }
            }
        }

        public IEqualityComparer<TKey> Comparer
        {
            get
            {
                lock (this.ThisLock)
                {
                    return _dic.Comparer;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.CheckLifeTime();

                    return _dic.Count;
                }
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    this.CheckLifeTime();

                    return _dic[key];
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    this.CheckLifeTime();

                    _volatileDictionary[key] = DateTime.UtcNow;
                    _dic[key] = value;
                }
            }
        }

        public bool Add(TKey key, TValue value)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                int count = _dic.Count;
                _dic[key] = value;
                _volatileDictionary[key] = DateTime.UtcNow;

                return (count != _dic.Count);
            }
        }

        public void Clear()
        {
            lock (this.ThisLock)
            {
                _volatileDictionary.Clear();
                _dic.Clear();
            }
        }

        public bool ContainsKey(TKey key)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.ContainsKey(key);
            }
        }

        public bool ContainsValue(TValue value)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.ContainsValue(value);
            }
        }

        public bool Remove(TKey key)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.Remove(key);
            }
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.TryGetValue(key, out value);
            }
        }

        ICollection<TKey> IDictionary<TKey, TValue>.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Keys;
                }
            }
        }

        ICollection<TValue> IDictionary<TKey, TValue>.Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this.Values;
                }
            }
        }

        void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
        {
            lock (this.ThisLock)
            {
                this.Add(key, value);
            }
        }

        bool IDictionary.IsFixedSize
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        bool IDictionary.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        ICollection IDictionary.Keys
        {
            get
            {
                lock (this.ThisLock)
                {
                    return (ICollection)this.Keys;
                }
            }
        }

        ICollection IDictionary.Values
        {
            get
            {
                lock (this.ThisLock)
                {
                    return (ICollection)this.Values;
                }
            }
        }

        object IDictionary.this[object key]
        {
            get
            {
                lock (this.ThisLock)
                {
                    return this[(TKey)key];
                }
            }
            set
            {
                lock (this.ThisLock)
                {
                    this[(TKey)key] = (TValue)value;
                }
            }
        }

        void IDictionary.Add(object key, object value)
        {
            lock (this.ThisLock)
            {
                this.Add((TKey)key, (TValue)value);
            }
        }

        bool IDictionary.Contains(object key)
        {
            lock (this.ThisLock)
            {
                return this.ContainsKey((TKey)key);
            }
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            throw new NotSupportedException();
        }

        void IDictionary.Remove(object key)
        {
            lock (this.ThisLock)
            {
                this.Remove((TKey)key);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly
        {
            get
            {
                lock (this.ThisLock)
                {
                    return false;
                }
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            lock (this.ThisLock)
            {
                this.Add(item.Key, item.Value);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return _dic.Contains(item);
            }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                ((IDictionary<TKey, TValue>)_dic).CopyTo(array, arrayIndex);
            }
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                return ((IDictionary<TKey, TValue>)_dic).Remove(keyValuePair);
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                lock (this.ThisLock)
                {
                    return true;
                }
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                return this.ThisLock;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                ((ICollection)_dic).CopyTo(array, index);
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            lock (this.ThisLock)
            {
                this.CheckLifeTime();

                foreach (var item in _dic)
                {
                    yield return item;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (this.ThisLock)
            {
                return this.GetEnumerator();
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

        public sealed class VolatileKeyCollection : ICollection<TKey>, IEnumerable<TKey>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<TKey> _collection;
            private readonly object _thisLock;

            internal VolatileKeyCollection(ICollection<TKey> collection, object thisLock)
            {
                _collection = collection;
                _thisLock = thisLock;
            }

            public TKey[] ToArray()
            {
                lock (this.ThisLock)
                {
                    var array = new TKey[_collection.Count];
                    _collection.CopyTo(array, 0);

                    return array;
                }
            }

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    _collection.CopyTo(array, arrayIndex);
                }
            }

            public int Count
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _collection.Count;
                    }
                }
            }

            bool ICollection<TKey>.IsReadOnly
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return true;
                    }
                }
            }

            void ICollection<TKey>.Add(TKey item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            void ICollection<TKey>.Clear()
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            bool ICollection<TKey>.Contains(TKey item)
            {
                lock (this.ThisLock)
                {
                    return _collection.Contains(item);
                }
            }

            bool ICollection<TKey>.Remove(TKey item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            bool ICollection.IsSynchronized
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return true;
                    }
                }
            }

            object ICollection.SyncRoot
            {
                get
                {
                    return this.ThisLock;
                }
            }

            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    this.CopyTo(array.OfType<TKey>().ToArray(), arrayIndex);
                }
            }

            public IEnumerator<TKey> GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    foreach (var item in _collection)
                    {
                        yield return item;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    return this.GetEnumerator();
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

        public sealed class VolatileValueCollection : ICollection<TValue>, IEnumerable<TValue>, ICollection, IEnumerable, IThisLock
        {
            private ICollection<TValue> _collection;
            private readonly object _thisLock;

            internal VolatileValueCollection(ICollection<TValue> collection, object thisLock)
            {
                _collection = collection;
                _thisLock = thisLock;
            }

            public TValue[] ToArray()
            {
                lock (this.ThisLock)
                {
                    var array = new TValue[_collection.Count];
                    _collection.CopyTo(array, 0);

                    return array;
                }
            }

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    _collection.CopyTo(array, arrayIndex);
                }
            }

            public int Count
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return _collection.Count;
                    }
                }
            }

            bool ICollection<TValue>.IsReadOnly
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return true;
                    }
                }
            }

            void ICollection<TValue>.Add(TValue item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            void ICollection<TValue>.Clear()
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            bool ICollection<TValue>.Contains(TValue item)
            {
                lock (this.ThisLock)
                {
                    return _collection.Contains(item);
                }
            }

            bool ICollection<TValue>.Remove(TValue item)
            {
                lock (this.ThisLock)
                {
                    throw new NotSupportedException();
                }
            }

            bool ICollection.IsSynchronized
            {
                get
                {
                    lock (this.ThisLock)
                    {
                        return true;
                    }
                }
            }

            object ICollection.SyncRoot
            {
                get
                {
                    return this.ThisLock;
                }
            }

            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                lock (this.ThisLock)
                {
                    this.CopyTo(array.OfType<TValue>().ToArray(), arrayIndex);
                }
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    foreach (var item in _collection)
                    {
                        yield return item;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                lock (this.ThisLock)
                {
                    return this.GetEnumerator();
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
}
