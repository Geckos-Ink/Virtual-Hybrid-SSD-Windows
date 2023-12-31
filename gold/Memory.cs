﻿/*
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
        public Tree<T> parent;
        public int level = 0;
        public char key;

        public List<string> Keys;
        public Dictionary<char, Tree<T>> tree;

        public string jumpTo;

        public T Value;

        public Tree()
        {
            Keys = new List<string>();
        }

        public Tree(Tree<T> parent, char key)
        {
            this.parent = parent;

            if(parent.tree == null)
                parent.tree = new Dictionary<char, Tree<T>>();

            parent.tree[key] = this;

            this.level = parent.level + 1;
            this.key = key;
        }

        public Tree<T> Get(string key)
        {
            key = key.ToLower();

            var l = this;
            for(int i=0; i<key.Length; i++)
            {
                if (!String.IsNullOrEmpty(l.jumpTo))
                {
                    var len = l.jumpTo.Length;
                    if ((i + len) <= key.Length && l.jumpTo == key.Substring(i, len))
                    {
                        i += len;

                        if (key.Length == i)
                            return l;
                    }
                    else
                        return null;
                }

                l = l.Get(key[i]);

                if (l == null)
                    break;

                if (key.Length - 1 == i)
                    return l;
                
            }

            return null;
        }

        public Tree<T> Get(char key) { 
            if(tree != null && tree.ContainsKey(key))
            {
                return tree[key];
            }

            return null;
        }

        public void Set(string key, T value)
        {
            var lkey = key.ToLower();

            var l = this;
            for (int i = 0; i < lkey.Length; i++)
            {
                if (!String.IsNullOrEmpty(l.jumpTo))
                {
                    var maxLen = lkey.Length - i;
                    var upTo = l.jumpTo.Length;
                    if(upTo > maxLen) upTo = maxLen;
     
                    int j = 0;
                    for (; j < upTo; j++)
                    {
                        if (l.jumpTo[j] != lkey[i + j])
                            break;
                    }

                    var div = l.jumpTo.Substring(j, l.jumpTo.Length - j);
                    l.jumpTo = l.jumpTo.Substring(0, j);

                    if (div.Length > 0)
                    {
                        var oldTree = l.tree;
                        l.tree = new Dictionary<char, Tree<T>>();

                        var tdiv = new Tree<T>(l, div[0]);
                        tdiv.tree = oldTree;
                        tdiv.Value = l.Value;
                        tdiv.jumpTo = div.Length > 1 ? div.Substring(1) : "";

                        l.Value = default(T);
                    }
                    else
                    {
                        l.tree = l.tree ?? new Dictionary<char, Tree<T>>();
                    }

                    if (l.jumpTo == lkey.Substring(i, l.jumpTo.Length))
                    {
                        i += l.jumpTo.Length;
                    }
                    else
                    {
                        var oldTree = l.tree;
                        l.tree = new Dictionary<char, Tree<T>>();

                        var shift = new Tree<T>(l, l.jumpTo[0]);
                        shift.tree = oldTree;
                        shift.Value = l.Value;
                        shift.jumpTo = l.jumpTo.Length > 1 ? l.jumpTo.Substring(1) : "";

                        l.jumpTo = "";
                        l.Value = default(T);
                    }

                    if (i == lkey.Length)
                        break;
                }

                if(l.tree == null)
                {
                    l.jumpTo = lkey.Substring(i);
                    break;
                }

                var k = lkey[i];
                var nextL = l.Get(k);

                if (nextL == null)
                    nextL = new Tree<T>(l, k);

                l = nextL;
            }

            l.Value = value;

            if (!Keys.Contains(key))
                Keys.Add(key);
        }


        public void Unset(string key)
        {
            var l = Get(key);

            l.Value = default(T);

            while(l != null)
            {
                if (l.Value == null && l.tree == null)
                {
                    l.parent?.tree?.Remove(l.key);
                }

                if (l.tree?.Count == 1)
                {
                    var keys = l.tree.Keys;
                    var k = keys.ElementAt(0);
                    var jt = l.tree[k];
                    l.jumpTo = (l.jumpTo ?? "") + k + (jt.jumpTo ?? "");
                    l.Value = jt.Value;
                    l.tree = jt.tree;
                }

                l = l.parent;
            }

            Keys.Remove(key);
        }

    }
}
