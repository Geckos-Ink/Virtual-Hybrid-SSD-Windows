using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
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
        public string Name = "drive";

        public Chucks chucks;

        public File root;
        public long MaxID = 0;

        public DB DB;

        public DB.Table<DB.FS> TableFS;

        public List<Drive> SSDDrives = new List<Drive>();
        public List<Drive> HDDDrives = new List<Drive>();

        public Settings Sets;

        public VHFS() {
            Sets = new Settings();

            DB = new DB(this);

            chucks = new Chucks(this);

            TableFS = DB.GetTable<DB.FS>();
            TableFS.SetKey("Parent", "ID");

            root = new File(true, 0, this);
        }

        public class Settings
        {
            public long chuckSize = 1024 * 1024; // 1 MB
            public long closeChuckAfter = 1000 * 10; // 10 seconds
            public int maxOpenedChucks = 32;
        }

        #region Drives 

        public Drive AddDrive(string letter, bool ssd)
        {
            var drive = new Drive(this, letter, ssd);

            if (ssd)
            {
                drive.id = (short)SSDDrives.Count;
                SSDDrives.Add(drive);
            }
            else
            {
                drive.id = (short)HDDDrives.Count;
                HDDDrives.Add(drive);
            }

            return drive;
        }

        int ssdTick = 0;
        int hddTick = 0;
        public Drive GetRandomDrive(bool ssd)
        {
            List<Drive> list = ssd ? SSDDrives : HDDDrives;
            var tick = ssd ? ssdTick++ : hddTick++;

            if (ssdTick >= int.MaxValue) ssdTick = 0;
            if (hddTick >= int.MaxValue) hddTick = 0;

            return list[tick % list.Count];
        }

        public class Drive
        {
            public VHFS vhfs;

            public short id;
            public string letter;
            public bool ssd;

            public DriveInfo info;

            public string Dir;

            public Drive(VHFS vhfs, string letter, bool ssd)
            {
                this.vhfs = vhfs;

                this.letter = letter;
                this.ssd = ssd;

                info = new DriveInfo(letter + ":\\");

                Dir = letter + ":\\vhssd";
                Static.CreateDirIfNotExists(Dir);

                Dir += "\\" + vhfs.Name + "\\";
                Static.CreateDirIfNotExists(Dir);
            }

            public double FreeSpace()
            {
                return (double)info.TotalFreeSpace / (double)info.TotalSize;
            }

            #region Stats

            public long OpenFiles = 0;

            public long AvgBytesRead = 0;
            Dictionary<long, long> bytesRead = new Dictionary<long, long>();
            public void addReadBytes(long length)
            {
                var now = Static.UnixTime;
                if (bytesRead.ContainsKey(now))
                {
                    bytesRead[now] += length;
                }
                else
                {
                    if (bytesRead.Count > 0)
                    {
                        var prev = bytesRead.First();
                        AvgBytesRead = (AvgBytesRead + prev.Value) / 2;
                        bytesRead.Remove(prev.Key);
                    }


                    bytesRead.Add(now, length);
                }
            }

            public long AvgBytesWrite = 0;
            Dictionary<long, long> bytesWrite = new Dictionary<long, long>();
            public void addWriteBytes(long length)
            {
                var now = Static.UnixTime;
                if (bytesWrite.ContainsKey(now))
                {
                    bytesWrite[now] += length;
                }
                else
                {
                    if (bytesWrite.Count > 0)
                    {
                        var prev = bytesWrite.First();
                        AvgBytesWrite = (AvgBytesWrite + prev.Value) / 2;
                        bytesWrite.Remove(prev.Key);
                    }

                    bytesWrite.Add(now, length);
                }
            }

            public long Traffic
            {
                get { return AvgBytesRead + AvgBytesRead; }
            }

            #endregion
        }

        #endregion

        #region GenericFS

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

        #endregion

        public class File
        {
            public VHFS vhfs;

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

            public File(long id, VHFS vhfs, File parent=null)
            {
                this.parent = parent;

                this.ID = id;
                SetFS(vhfs);

                Load();
            }

            public void SetFS(VHFS vhfs)
            {
                if (this.vhfs != null || vhfs == null)
                    return;

                this.vhfs = vhfs;

                if (this.ID == -1)
                    this.ID = ++this.vhfs.MaxID;
            }

            #region LazyLoadSave

            DB.FS lastFS;

            bool loaded = false;
            void Load(bool lazy=false)
            {
                if (loaded) return;

                var fs = new FS();
                fs.ID = ID;
                fs.Parent = parent == null ? -1 : parent.ID;

                fs = this.vhfs.TableFS.Get(fs, "Parent,ID");

                if (fs == null)
                {
                    loaded = true;
                    return;
                }

                name = fs.Name.ToString();
                isDirectory = fs.IsDirectory;

                attributes.FileAttributes = fs.FileAttributes;
                attributes.GrantedAccess = fs.GrantedAccess;
                attributes.LastAccessTime = fs.LastAccessTime;
                attributes.LastWriteTime = fs.LastWriteTime;
                attributes.AllocationSize = fs.AllocationSize;
                attributes.ChangeTime = fs.ChangeTime;
                attributes.CreationTime = fs.CreationTime;
                attributes.FileSize = fs.FileSize;
                attributes.SecurityDescription = fs.SecurityDescription;

                /// Load tree
                filesIDs = fs.Files;
                if(!lazy)
                    LoadFiles();

                lastFS = fs;
                loaded = true;
            }

            bool loadedFiles = false;

            long[] filesIDs;
            void LoadFiles()
            {
                if (isDirectory && !loadedFiles)
                {
                    foreach (var fid in filesIDs)
                    {
                        var file = new File(fid, this.vhfs, this);
                        file.Load(true);
                        files.Set(file.name, file);
                    }
                }

                loadedFiles = true;
            }

            long lastSave = 0;
            bool changes = false;
            void Save()
            {
                var fs = new FS();

                fs.ID = ID;
                fs.Parent = parent != null ? parent.ID : -1;

                fs.Name = name.ToCharArray();

                fs.FileAttributes = attributes.FileAttributes;
                fs.GrantedAccess = attributes.GrantedAccess;
                fs.LastAccessTime = attributes.LastAccessTime;
                fs.LastWriteTime = attributes.LastWriteTime;
                fs.AllocationSize = attributes.AllocationSize;
                fs.ChangeTime = attributes.ChangeTime;
                fs.CreationTime = attributes.CreationTime;
                fs.FileSize = attributes.FileSize;
                fs.SecurityDescription = attributes.SecurityDescription;

                var tFS = vhfs.DB.GetType(typeof(DB.FS));
                if (!tFS.CompareObjs(fs, lastFS))
                {
                    this.vhfs.TableFS.Set(fs, "Parent,ID");
                    lastFS = fs;
                    lastSave = Static.UnixTimeMS;
                }
            }

            #endregion

            #region Directory

            public void AddFile(File file)
            {
                LoadFiles();

                file.parent = this;
                files.Set(file.name, file);

                changes = true;
            }

            public File GetFile(string name)
            {
                LoadFiles();

                if (String.IsNullOrEmpty(name))
                    return this;

                return files.Get(name)?.value;
            }

            public List<string> ListFiles()
            {
                LoadFiles();

                return files?.keys;
            }

            public void Remove()
            {
                parent.Remove(name);
            }

            public void Remove(string name)
            {
                LoadFiles();

                files.Unset(name);

                changes = true;
            }

            #endregion

            #region File

            public void Read(IntPtr Buffer, UInt64 Offset, UInt32 Length,  out UInt32 PBytesTransferred)
            {
                var bytes = vhfs.chucks.Read(ID, (long)Offset, Length);
                Marshal.Copy(bytes, 0, Buffer, bytes.Length);
                PBytesTransferred = Length;
            }

            public void Write(IntPtr Buffer,  UInt64 Offset, UInt32 Length, Boolean WriteToEndOfFile, Boolean ConstrainedIo, out UInt32 PBytesTransferred, out Fsp.Interop.FileInfo FileInfo)
            {
                Byte[] Bytes = new byte[Length];
                Marshal.Copy(Buffer, Bytes, 0, Bytes.Length);
               
                vhfs.chucks.Write(ID, (long)Offset, Bytes);

                if (WriteToEndOfFile || attributes.FileSize < Offset + Length)
                {
                    SetSize(Offset + Length);
                }

                PBytesTransferred = (UInt32)Bytes.Length;

                FileInfo = GetFileInfo();
            }

            public void SetSize(UInt64 NewSize, Boolean SetAllocationSize = false)
            {
                vhfs.chucks.Resize(ID, (long)attributes.FileSize, (long)NewSize);
                attributes.FileSize = NewSize;
            }

            public void Flush()
            {
                //todo
            }

            #endregion

            public void Dispose()
            {
                Save();
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
