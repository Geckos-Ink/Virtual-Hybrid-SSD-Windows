using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    // Virtual Hybrid File System
    public class VHFS
    {
        public VHFS() {
            var test = new Tree<string>();
            test.Set("ciao", "come");
            test.Set("stai", "oggi");
            test.Set("bene", "e te?");

            var ciao = test.Get("ciao");
            var stai = test.Get("stai");
            var bene = test.Get("bene");

            Console.WriteLine("test");
        }

        public class File
        {
            public VHFS fs;

            public Int64 ID;

            public string name;
            public File parent;
            public bool isDirectory;

            // File
            public Int64 length;

            // Directory
            public Dictionary<string, File> files; 
        }
    }
}
