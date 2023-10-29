using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static VHSSD.DB;
using static VHSSD.Ptfs;

namespace VHSSD
{
    // Virtual Hybrid File System
    public class VHFS
    {
        public File root;
        public long MaxID = 0;

        public DB DB;

        public DB.Table<DB.FS> TableFS;

        public VHFS() {
            DB = new DB(this);
            TableFS = DB.GetTable<DB.FS>();

            root = new File(true, 0, this);
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
            file.SetFS(this);

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

            public long ID;

            public string name;
            public File parent;
            public bool isDirectory;

            public bool open = false;

            // Directory
            public Tree<File> files;

            // Attributes
            public Attributes attributes;

            public File(bool isDirectory, long id=-1, VHFS vhfs=null)
            {
                this.ID = id;
                SetFS(vhfs);

                this.isDirectory = isDirectory;

                if (isDirectory)
                {
                    files = new Tree<File>();

                    attributes.FileAttributes = (uint)FileAttributes.Directory;
                }
            }

            public void SetFS(VHFS vhfs)
            {
                if (fs != null || vhfs == null)
                    return;

                fs = vhfs;

                if (this.ID == -1)
                    this.ID = ++fs.MaxID;
            }

            #region LazyLoadSave

            bool loaded = false;

            void Load()
            {
                if (loaded) return;

                var fs = new FS();
                fs.ID = ID;
                fs.Parent = parent == null ? -1 : parent.ID;

            }

            #endregion

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

            public void Remove()
            {
                parent.Remove(name);
            }

            public void Remove(string name)
            {
                files.Unset(name);
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
                public byte[] SecurityDescription;
            }
        }
    }
}
