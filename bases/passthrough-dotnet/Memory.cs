using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Tree<T>
    {
        public int level = 0;
        public Dictionary<char, Tree<T>> tree;

        public T value;

        public Tree() { }

        public Tree(Tree<T> parent)
        {
            this.level = parent.level + 1;
        }

        public T Get(string key)
        {
            var l = this;
            for(int i=0; i<key.Length; i++)
            {
                l = l.Get(key[i]);

                if (l == null)
                    break;

                if (key.Length-1 == i)
                    return l.value;
            }

            return default(T);
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
            var l = this;
            for (int i = 0; i < key.Length; i++)
            {
                var nextL = l.Get(key[i]);

                if(nextL == null)
                {
                    nextL = new Tree<T>(l);
                    l.tree[key[i]] = nextL;
                }
            }

            l.value = value;
        }

    }
}
