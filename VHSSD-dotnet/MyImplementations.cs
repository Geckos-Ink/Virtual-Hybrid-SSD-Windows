using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class OrderedDictionary<TKey, TValue>
    {
        private Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();
        private List<KeyValuePair<TKey, TValue>> insertionOrder = new List<KeyValuePair<TKey, TValue>>();

        public void Add(TKey key, TValue value)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
                insertionOrder.Add(new KeyValuePair<TKey, TValue>(key, value));
            }
            else
            {
                throw new ArgumentException("An item with the same key has already been added.");
            }
        }

        public bool Has(TKey key)
        {
            return dictionary.ContainsKey(key);
        }

        public void Remove(TKey key)
        {
            var kv = new KeyValuePair<TKey, TValue>(key, dictionary[key]);
            dictionary.Remove(key);
            insertionOrder.Remove(kv);
        }

        public KeyValuePair<TKey, TValue> Last()
        {
            return insertionOrder.Last();
        }

        public TValue this[TKey key]
        {
            get { return dictionary[key]; }
            set
            {
                if (dictionary.ContainsKey(key))
                {
                    dictionary[key] = value;
                    for (int i = 0; i < insertionOrder.Count; i++)
                    {
                        if (EqualityComparer<TKey>.Default.Equals(insertionOrder[i].Key, key))
                        {
                            insertionOrder[i] = new KeyValuePair<TKey, TValue>(key, value);
                            break;
                        }
                    }
                }
                else
                {
                    throw new KeyNotFoundException("The given key was not present in the dictionary.");
                }
            }
        }

        public IEnumerable<KeyValuePair<TKey, TValue>> Items
        {
            get { return insertionOrder; }
        }
    }
}
