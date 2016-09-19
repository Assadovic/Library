using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Runtime.Serialization;

namespace Library
{
    public struct InformationContext
    {
        private string _key;
        private object _value;

        public InformationContext(string key, object value)
        {
            _key = key;
            _value = value;
        }

        public string Key
        {
            get
            {
                return _key;
            }
            private set
            {
                _key = value;
            }
        }

        public object Value
        {
            get
            {
                return _value;
            }
            private set
            {
                _value = value;
            }
        }

        public override string ToString()
        {
            return string.Format("Key = {0}, Value = {1}", this.Key, this.Value);
        }
    }

    public class Information : IEnumerable<InformationContext>
    {
        private Dictionary<string, object> _contexts;

        public Information(IEnumerable<InformationContext> contexts)
        {
            _contexts = new Dictionary<string, object>();

            foreach (var item in contexts)
            {
                _contexts.Add(item.Key, item.Value);
            }
        }

        public object this[string propertyName]
        {
            get
            {
                return _contexts[propertyName];
            }
        }

        public bool Contains(string propertyName)
        {
            return _contexts.ContainsKey(propertyName);
        }

        public IEnumerator<InformationContext> GetEnumerator()
        {
            foreach (var pair in _contexts)
            {
                yield return new InformationContext(pair.Key, pair.Value);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
