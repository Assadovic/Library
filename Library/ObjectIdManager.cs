using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Library
{
    public class ObjectIdManager<T> : IEnumerable<KeyValuePair<int, T>>, IEnumerable
    {
        private Dictionary<T, int> _objectMap = new Dictionary<T, int>();
        private Dictionary<int, T> _idMap = new Dictionary<int, T>();
        private Random _random = new Random();

        private readonly object _thisLock = new object();

        public int Add(T item)
        {
            int id;

            for (;;)
            {
                id = _random.Next(0, int.MaxValue);
                if (!_idMap.ContainsKey(id)) break;
            }

            _objectMap.Add(item, id);
            _idMap.Add(id, item);

            return id;
        }

        public int GetId(T item)
        {
            int id;
            if (_objectMap.TryGetValue(item, out id)) return id;

            throw new KeyNotFoundException();
        }

        public T GetItem(int id)
        {
            T item;
            if (_idMap.TryGetValue(id, out item)) return item;

            throw new KeyNotFoundException();
        }

        public void Remove(int id)
        {
            T item;
            if (!_idMap.TryGetValue(id, out item)) return;

            _idMap.Remove(id);
            _objectMap.Remove(item);
        }

        public void Clear()
        {
            _objectMap.Clear();
            _idMap.Clear();
        }

        public IEnumerator<KeyValuePair<int, T>> GetEnumerator()
        {
            foreach (var item in _idMap)
            {
                yield return item;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
