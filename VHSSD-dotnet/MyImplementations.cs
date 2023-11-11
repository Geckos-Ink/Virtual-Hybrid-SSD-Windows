/*
 *	Virtual Hybrid SSD for Windows
 *	VHSSD  Copyright (C) 2023  Riccardo Cecchini <rcecchini.ds@gmail.com>
 *
 *	This program is free software: you can redistribute it and/or modify
 *	it under the terms of the GNU General Public License as published by
 *	the Free Software Foundation, either version 3 of the License, or
 *	(at your option) any later version.
 *
 *	This program is distributed in the hope that it will be useful,
 *	but WITHOUT ANY WARRANTY; without even the implied warranty of
 *	MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *	GNU General Public License for more details.
 *
 *	You should have received a copy of the GNU General Public License
 *	along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

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
        public static Debug Debug = new Debug();

        /// <summary>
        /// Enable it just for debug purposes: it reset ALL data at every startup
        /// </summary>
        public static bool DebugResetEnv = true;

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

        public static long Ticks
        {
            get
            {
                return DateTimeOffset.UtcNow.Ticks;
            }
        }

        public static ulong FileTime
        {
            get
            {
                return (ulong)DateTime.Now.ToFileTimeUtc();
            }
        }

        public static void CreateDirIfNotExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                if (DebugResetEnv)
                {
                    Directory.Delete(path, true);
                    Directory.CreateDirectory(path);
                }
            }
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
                insertionOrder.Insert(InsertAt(key), new KeyValuePair<TKey, TValue>(key, value));
                
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

        public int IndexOfKey(TKey key)
        {
            var pos = InsertAt(key);

            if (pos == insertionOrder.Count) return -1;

            int compare = Comparer<TKey>.Default.Compare(insertionOrder[pos].Key, key);

            if (compare == 0)
                return pos;

            return -1;
        }

        // Horrible implementation (which require not-so-much memory footprint) - probably unused
        public TKey IndexOf(TValue value)
        {
            foreach(var kv in insertionOrder)
            {
                if(kv.Value.Equals(value))
                    return kv.Key;
            }

            try
            {
                return (TKey)Convert.ChangeType(-1, typeof(TKey));
            }
            catch { }

            return default(TKey);
        }

        public int InsertAt(TKey key)
        {
            if (insertionOrder.Count == 0)
                return 0;

            // Improve the algorithm
            int precision = insertionOrder.Count / 2;
            int pos = precision;

            int compare = 0;
            int comparePP = 0;
            while (true)
            {
                if (precision <= 1)
                {
                    precision = 1;
                    comparePP = compare;
                }

                compare = Comparer<TKey>.Default.Compare(insertionOrder[pos].Key, key);

                bool over = comparePP != 0 && comparePP != compare;
                if (over && compare == 1) pos--;

                if (compare == 0 || over)
                    return pos;

                pos -= precision * compare;

                if (pos < 0)
                {
                    pos = 0;
                    if (comparePP == 0 || compare > 0) break;
                }
                else if (pos >= insertionOrder.Count)
                {
                    pos = insertionOrder.Count;
                    if (comparePP == 0 || compare < 0) break;
                }


                precision /= 2;
            }

            return pos;
        }

        public TValue this[TKey key]
        {
            get { return dictionary[key]; }
            set
            {
                if (dictionary.ContainsKey(key))
                {
                    dictionary[key] = value;
                    insertionOrder[IndexOfKey(key)] = new KeyValuePair<TKey, TValue>(key, value);
                }
                else
                {
                    throw new KeyNotFoundException("The given key was not present in the dictionary.");
                }
            }
        }

        public List<KeyValuePair<TKey, TValue>> Items
        {
            get { return insertionOrder; }
        }
    }
}
