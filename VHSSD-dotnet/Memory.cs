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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    /// <summary>
    /// This class was full of hope and a total repetance
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Tree<T>
    {
        public List<string> Keys;
        public Dictionary<string, T> tree;

        public Tree()
        {
            Keys = new List<string>();
            tree = new Dictionary<string, T>();
        }

        public T Get(string key)
        {
            var lkey = key.ToLower();

            T o;
            if (tree.TryGetValue(lkey, out o))
                return o;

            return default(T);
        }

        public void Set(string key, T value)
        {
            var lkey = key.ToLower();

            Keys.Add(key);
            tree.Add(lkey, value);
        }

        public void Unset(string key)
        {
            var lkey = key.ToLower();

            Keys.Remove(key);
            tree.Remove(lkey);
        }
    }
}
