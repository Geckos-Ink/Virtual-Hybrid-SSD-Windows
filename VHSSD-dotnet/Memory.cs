using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Tree<T>
    {
        public Tree<T> parent;
        public int level = 0;
        public char key;

        public List<string> keys;
        public Dictionary<char, Tree<T>> tree;

        public string jumpTo;

        public T value;

        public Tree()
        {
            keys = new List<string>();
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
                    if ((i + len + 1) <= key.Length && l.jumpTo == key.Substring(i + 1, len))
                        i += len;

                    if (key.Length - 1 == i)
                        return l;
                }
                else
                {
                    l = l.Get(key[i]);

                    if (l == null)
                        break;

                    if (key.Length - 1 == i)
                        return l;
                }
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
            bool exists = true;

            var lkey = key.ToLower();

            var l = this;
            for (int i = 0; i < lkey.Length; i++)
            {
                if (l.tree == null && String.IsNullOrEmpty(l.jumpTo))
                {
                    l.jumpTo = lkey.Substring(i);
                    break;
                }
                else
                {
                    if (!String.IsNullOrEmpty(l.jumpTo))
                    {
                        l.tree = new Dictionary<char, Tree<T>>();
                        var jt = l.jumpTo;
                        l.jumpTo = null;
                        l.Set(jt, l.value);
                        l.value = default(T);
                    }

                    var nextL = l.Get(lkey[i]);

                    if (nextL == null)
                    {
                        exists = false;
                        nextL = new Tree<T>(l, lkey[i]);
                        l = nextL;
                    }
                }
            }

            if(!exists)
                keys.Add(key);

            l.value = value;
        }


        public void Unset(string key)
        {
            var l = Get(key);

            l.value = default(T);
           
            while(l != null)
            {
                if (l.value == null && l.tree == null)
                {
                    l.parent?.tree.Remove(l.key);
                }

                if (l.tree?.Count == 1)
                {
                    var keys = l.tree.Keys;
                    var k = keys.ElementAt(0);
                    var jt = l.tree[k];
                    l.jumpTo = k + (jt.jumpTo ?? "");
                    l.value = jt.value;
                    l.tree = jt.tree;
                    l.tree = null;
                }

                l = l.parent;
            }

            keys.Remove(key);
        }

    }
}
