using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    // Virtual Hybrid File System
    public class VHFS
    {
        public File root;

        public VHFS() {
            root = new File(true);
        }

        void testTree()
        {
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
            public Tree<File> files;

            // Attributes
            public Attributes attributes;

            public File(bool isDirectory)
            {
                this.isDirectory = isDirectory;

                if (isDirectory)
                {
                    files = new Tree<File>();
                }
            }

            public void AddFile(File file)
            {
                files.Set(file.name, file);
            }

            public void AddFile(File file, string path)
            {
                var dirs = path.Split('\\');
                var name = dirs[dirs.Length - 1];

                var cfile = this;
                for(int d=0; d<dirs.Length-2; d++)
                {
                    var dir = dirs[d];
                    cfile = cfile.GetFile(dir);
                }

                cfile.AddFile(file);
            }

            public File GetFile(string name)
            {
                return files.Get(name)?.value;
            }

            public Fsp.Interop.FileInfo GetFileInfo()
            {
                var res = new Fsp.Interop.FileInfo();

                res.FileAttributes = attributes.FileAttributes;
                res.ReparseTag = 0;
                res.FileSize = attributes.FileSize;
                res.AllocationSize = attributes.AllocationSize;
                res.CreationTime = attributes.CreationTime;
                res.LastAccessTime = attributes.LastAccessTime;
                res.LastWriteTime = attributes.LastWriteTime;
                res.ChangeTime = attributes.ChangeTime;
                res.IndexNumber = 0;
                res.HardLinks = 0;

                // FileInfo.AllocationSize = (FileInfo.FileSize + ALLOCATION_UNIT - 1) / ALLOCATION_UNIT * ALLOCATION_UNIT;

                return res;
            }

            public struct Attributes
            {
                public uint FileAttributes;
                public UInt32 GrantedAccess;
                public UInt64 FileSize;
                public ulong AllocationSize;
                public ulong CreationTime;
                public ulong LastAccessTime;
                public ulong LastWriteTime;
                public ulong ChangeTime;

                public uint SecurityDescriptionLen;
                public byte[] SecurityDescription;
            }
        }
    }
}
