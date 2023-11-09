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
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static VHSSD.DB;
using static VHSSD.Ptfs;
using static VHSSD.VHFS;

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
        public DB.Table<DB.Drive> TableDrive;

        public List<Drive> AllDrives = new List<Drive>();
        public List<Drive> SSDDrives = new List<Drive>();
        public List<Drive> HDDDrives = new List<Drive>();

        public Settings Sets;

        public Sync Sync;

        public VHFS() {
            Sets = new Settings();

            DB = new DB(this);

            chucks = new Chucks(this);

            TableFS = DB.GetTable<DB.FS>();
            TableFS.SetKey("Parent", "ID");

            TableDrive = DB.GetTable<DB.Drive>();
            TableDrive.SetKey("ID");

            root = new File(true, 0, this);

            // Start sync
            Sync = new Sync(this);
        }

        public class Settings
        {
            public long chuckSize = 1024 * 1024; // 1 MB
            public long closeChuckAfter = 1000 * 10; // 10 seconds
            public int maxOpenedChucks = 32;

            public long saveIterateStreamAfter = 1000 * 5; // 5 seconds
        }

        public void Close()
        {
            Sync.Close();
        }

        #region Drives 

        public long TotalSize = 0;

        public long UsedBytes
        {
            get
            {
                long res = 0;
                foreach (var disk in AllDrives)
                    res += disk.row.UsedBytes;

                return res;
            }
        }

        public Drive AddDrive(string letter, bool ssd)
        {
            var drive = new Drive(this, letter, ssd);

            if (ssd)
                SSDDrives.Add(drive);
            else
                HDDDrives.Add(drive);

            AllDrives.Add(drive);

            return drive;
        }

        public Drive GetBestDrive(bool ssd)
        {
            List<Drive> list = ssd ? SSDDrives : HDDDrives;

            foreach (Drive drive in list)
                drive.UsedSpace();

            var ordered = list.OrderBy(d => d.lastUsedSpace);

            return ordered.Last();
        }

        public class Drive
        {
            public VHFS vhfs;

            public short id;
            public string letter;
            public bool ssd;

            public DriveInfo info;

            public string Dir;

            public DB.Drive row;

            public long MaxSize = -1;

            public Drive(VHFS vhfs, string letter, bool ssd)
            {
                this.vhfs = vhfs;

                id = (short)(vhfs.AllDrives.Count);

                this.letter = letter;
                this.ssd = ssd;

                info = new DriveInfo(letter + ":\\");

                Dir = letter + ":\\vhssd";
                Static.CreateDirIfNotExists(Dir);

                Dir += "\\" + vhfs.Name + "\\";
                Static.CreateDirIfNotExists(Dir);

                var dbRow = new DB.Drive();
                dbRow.ID = id;

                row = vhfs.TableDrive.Get(dbRow) ?? dbRow;

                MaxSize = (info.TotalSize) / 2;

                // Init stats
                TotalBytes = new Stats();
                BytesRead = new Stats(TotalBytes);
                BytesWrite = new Stats(TotalBytes);
            }

            public void Close()
            {
                vhfs.TableDrive.Set(row);
            }

            public double lastUsedSpace = 0;
            public double UsedSpace()
            {
                lastUsedSpace = (double) row.UsedBytes / MaxSize;
                return lastUsedSpace;
            }

            #region Stats

            public long OpenFiles = 0;

            public Stats TotalBytes;
            public Stats BytesRead;
            public Stats BytesWrite;

            public void addReadBytes(long length)
            {
                BytesRead.Add(length);
            }

            public void addWriteBytes(long length)
            {
                BytesWrite.Add(length);
            }

            public bool OverUsed
            {
                get
                {
                    return TotalBytes.Val > TotalBytes.Avg;
                }
            }

            public bool ReadOverUsed
            {
                get
                {
                    return BytesRead.Val > BytesRead.Avg;
                }
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

            public string name = "";
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

                    if (id == 0) 
                        Load(); // it's root directory
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

            public bool loaded = false;

            public void Load(bool lazy=false)
            {
                if (loaded) return;

                var fs = new FS();
                fs.ID = ID;
                fs.Parent = parent == null ? -1 : parent.ID;

                fs = this.vhfs.TableFS.Get(fs, "Parent,ID");

                if (fs == null)
                {
                    return; // uhm, this shouldn't be normal
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
                //attributes.SecurityDescription = fs.SecurityDescription; // FUCK YOU 

                lastFS = fs.Clone<DB.FS>();

                /// Load tree
                if (!lazy)
                    LoadFiles();

                loaded = true;
            }

            bool loadedFiles = false;

            void LoadFiles()
            {
                if (isDirectory && !loadedFiles)
                {
                    if (lastFS != null)
                    {
                        foreach (var fid in lastFS.Files)
                        {
                            var file = new File(fid, this.vhfs, this);
                            file.Load(true);
                            files.Set(file.name, file);
                        }
                    }
                }

                loadedFiles = true;
            }

            // Pretty useless vars
            long lastSave = 0;
            bool changes = false;

            void Save()
            {
                var fs = new FS();

                fs.ID = ID;
                fs.Parent = parent != null ? parent.ID : -1;

                fs.Name = name;

                fs.IsDirectory = isDirectory;

                fs.FileAttributes = attributes.FileAttributes;
                fs.GrantedAccess = attributes.GrantedAccess;
                fs.LastAccessTime = attributes.LastAccessTime;
                fs.LastWriteTime = attributes.LastWriteTime;
                fs.AllocationSize = attributes.AllocationSize;
                fs.ChangeTime = attributes.ChangeTime;
                fs.CreationTime = attributes.CreationTime;
                fs.FileSize = attributes.FileSize;
                fs.SecurityDescription = attributes.SecurityDescription;

                // Get file tree
                if (isDirectory)
                {
                    fs.Files = new long[files.Keys.Count];

                    var f = 0;
                    foreach (var key in files.Keys)
                        fs.Files[f] = files.Get(files.Keys[f++]).Value.ID;
                }

                var tFS = vhfs.DB.GetType(typeof(DB.FS));
                if (!tFS.CompareObjs(fs, lastFS) || true) // seems to not work
                {
                    this.vhfs.TableFS.Set(fs);
                    lastFS = fs;
                    lastSave = Static.UnixTimeMS;
                }

                lastSave = 0;
                changes = false;
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

                return files.Get(name)?.Value;
            }

            public List<string> ListFiles()
            {
                LoadFiles();

                return files?.Keys;
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
                if (!vhfs.chucks.chucks.ContainsKey(ID))
                    return;

                foreach(var chuck in vhfs.chucks.chucks[ID])
                {
                    chuck.Value.Flush();
                }
            }

            #endregion

            public void Dispose()
            {
                Save();
            }

            public Fsp.Interop.FileInfo GetFileInfo()
            {
                Load(true);

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
