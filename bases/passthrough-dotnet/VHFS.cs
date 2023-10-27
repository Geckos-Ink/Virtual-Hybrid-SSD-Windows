using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static VHSSD.Ptfs;

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

        public File GetFile(string path)
        {
            var dirs = path.Substring(1).Split('\\');

            var cfile = root;
            foreach(var dir in dirs)
            {
                cfile = cfile.GetFile(dir);

                if (cfile == null)
                    break;
            }

            return cfile;
        }

        public void AddFile(File file, string path)
        {
            var dirs = path.Substring(1).Split('\\');
            file.name = dirs[dirs.Length - 1];

            var cfile = root;
            for (int d = 0; d < dirs.Length - 2; d++)
            {
                var dir = dirs[d];
                cfile = cfile.GetFile(dir);
            }

            cfile.AddFile(file);
        }

        public void Rename(string fileName, string newFileName, bool replace)
        {
            //todo: handle replace
            var file = GetFile(fileName);
            file.parent.files.Unset(file.name);

            AddFile(file, newFileName);
        }


        public class File
        {
            public VHFS fs;

            public Int64 ID;

            public string name;
            public File parent;
            public bool isDirectory;

            public bool open = false;

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

                    attributes.FileAttributes = (uint)FileAttributes.Directory;
                }
            }

            #region Directory

            public void AddFile(File file)
            {
                file.parent = this;
                files.Set(file.name, file);
            }


            public File GetFile(string name)
            {
                if (String.IsNullOrEmpty(name))
                    return this;

                return files.Get(name)?.value;
            }

            public List<string> ListFiles()
            {
                return files?.keys;
            }

            #endregion

            #region File

            // Temporary ram-disk solution
            public byte[] bytes = new byte[0];

            public void Read(IntPtr Buffer, UInt64 Offset, UInt32 Length,  out UInt32 PBytesTransferred)
            {
                Byte[] Bytes = new byte[Length];
                Array.Copy(bytes, (int)Offset, Bytes, 0, Length);
                PBytesTransferred = Length;
                Marshal.Copy(Bytes, 0, Buffer, Bytes.Length);
            }

            public void Write(IntPtr Buffer,  UInt64 Offset, UInt32 Length, Boolean WriteToEndOfFile, Boolean ConstrainedIo, out UInt32 PBytesTransferred, out Fsp.Interop.FileInfo FileInfo)
            {
                Byte[] Bytes = new byte[Length];
                Marshal.Copy(Buffer, Bytes, 0, Bytes.Length);
                
                if(attributes.FileSize < Offset + Length)
                {
                    SetSize(Offset + Length);
                }

                Array.Copy(Bytes, 0, bytes, (int)Offset, Length);

                if (WriteToEndOfFile)
                {
                    bytes = bytes.Skip((int)(Offset+Length)).ToArray();
                }

                PBytesTransferred = (UInt32)Bytes.Length;

                FileInfo = GetFileInfo();
            }

            public void SetSize(UInt64 NewSize, Boolean SetAllocationSize = false)
            {
                var largerBytes = new byte[NewSize];
                Array.Copy(bytes, largerBytes, bytes.Length);
                bytes = largerBytes;

                attributes.FileSize = NewSize;
            }

            public void Flush()
            {
                //todo
            }

            #endregion

            public void Dispose()
            {
                //todo
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
