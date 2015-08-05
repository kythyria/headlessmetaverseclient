using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HeadlessSlClient
{

    class CacheDictionary<TKey, TValue> : IDictionary<TKey, TValue>
        where TValue : class
    {
        Dictionary<TKey, WeakReference<TValue>> store = new Dictionary<TKey,WeakReference<TValue>>();

        public void Add(TKey key, TValue value)
        {
            WeakReference<TValue> slot;
            if(store.TryGetValue(key, out slot))
            {
                slot.SetTarget(value);
            }
        }

        public bool ContainsKey(TKey key)
        {
            WeakReference<TValue> slot;
            if (store.TryGetValue(key, out slot))
            {
                TValue thing;
                if(slot.TryGetTarget(out thing))
                { return true; }
                else
                {
                    store.Remove(key);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public ICollection<TKey> Keys
        {
            get
            {
                Cleanup();
                return store.Keys;
            }
        }

        public bool Remove(TKey key)
        {
            Cleanup();
            return store.Remove(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            WeakReference<TValue> slot;
            if (store.TryGetValue(key, out slot))
            {
                if(slot.TryGetTarget(out value))
                {
                    return true;
                }
                else
                {
                    Cleanup();
                    value = default(TValue);
                    return false;
                }
            }
            value = default(TValue);
            return false;
        }

        public ICollection<TValue> Values
        {
            get
            {
                var results = new List<TValue>();
                foreach(var slot in store.Values)
                {
                    TValue result;
                    if(slot.TryGetTarget(out result))
                    {
                        results.Add(result);
                    }
                }
                return results;
            }
        }

        public TValue this[TKey key]
        {
            get
            {
                var slot = store[key];
                TValue target;
                if(slot.TryGetTarget(out target))
                {
                    return target;
                }
                else
                {
                    store.Remove(key);
                    throw new KeyNotFoundException();
                }
            }
            set
            {
                store[key] = new WeakReference<TValue>(value);
            }
        }

        public void Clear()
        {
            store.Clear();
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            int idx = 0;
            foreach (var i in store)
            {
                TValue value;
                if(i.Value.TryGetTarget(out value))
                {
                    array[idx++] = new KeyValuePair<TKey,TValue>(i.Key, value);
                }
            }
        }

        public int Count
        {
            get { return store.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            store.Add(item.Key, new WeakReference<TValue>(item.Value));
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            WeakReference<TValue> slot;
            if (store.TryGetValue(item.Key, out slot))
            {
                TValue value;
                slot.TryGetTarget(out value);
                return value == item.Value;
            }
            return false;
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            throw new NotImplementedException();
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        //----

        private void Cleanup()
        {

        }
    }
}
