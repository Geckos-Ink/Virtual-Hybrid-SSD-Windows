using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public static class Static
    {
        public static long UnixTimeMS
        {
            get
            {
                return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public static long UnixTime
        {
            get
            {
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }
        }

        public static void CreateDirIfNotExists(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
    }

    public class OrderedDictionary<TKey, TValue>
    {
        private Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();
        private List<KeyValuePair<TKey, TValue>> insertionOrder = new List<KeyValuePair<TKey, TValue>>();

        public void Clear()
        {
            dictionary.Clear();
            insertionOrder.Clear();
        }

        public void Add(TKey key, TValue value)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
                insertionOrder.Add(new KeyValuePair<TKey, TValue>(key, value));
                insertionOrder = insertionOrder.OrderBy(x => x.Key).ToList();
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

        public int IndexOf(TKey key)
        {
            // Improve the algorithm
            int precision = insertionOrder.Count / 2;
            int pos = precision;

            int compare = 0;
            while (true)
            {
                precision /= 2;
                if (precision <= 1)
                    precision = 1;


                compare = Comparer<TKey>.Default.Compare(insertionOrder[pos].Key, key);

                if (compare == 0)
                    return pos;
                
                pos -= precision * compare;

                if (pos < 0 || pos > insertionOrder.Count) break;
            }

            return -1;
        }

        public TValue this[TKey key]
        {
            get { return dictionary[key]; }
            set
            {
                if (dictionary.ContainsKey(key))
                {
                    dictionary[key] = value;
                    insertionOrder[IndexOf(key)] = new KeyValuePair<TKey, TValue>(key, value);
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
