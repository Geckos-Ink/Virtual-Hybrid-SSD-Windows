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
using System.Threading;
using System.Threading.Tasks;
using static VHSSD.DB;
using static VHSSD.Ptfs;
using static VHSSD.VHFS;
using static VHSSD.VHFS.File;

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
                if (String.IsNullOrEmpty(dir))
                    return cfile;

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
            for (int d = 0; d < dirs.Length - 1; d++)
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

                checkDirectory();

                if (ID == 0)
                {
                    Load(false); // it's root directory
                    loadedFiles = true;
                }
            }

            public File(long id, VHFS vhfs, File parent=null)
            {
                this.parent = parent;

                this.ID = id;
                SetFS(vhfs);

                Load(true);
            }

            bool _checkDirectoryChecked = false;
            void checkDirectory()
            {
                if (isDirectory && !_checkDirectoryChecked)
                {
                    files = new Tree<File>();
                    attributes.FileAttributes = (uint)FileAttributes.Directory;

                    _checkDirectoryChecked = true;
                }
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

            public void Load(bool lazy)
            {
                if (loaded) return;
                loaded = true;

                var fs = new FS();
                fs.ID = ID;
                fs.Parent = parent == null ? -1 : parent.ID;

                fs = this.vhfs.TableFS.Get(fs, "Parent,ID");

                if (fs == null)
                    return; // uhm, this shouldn't be normal

                name = fs.Name.ToString();
                isDirectory = fs.IsDirectory;

                checkDirectory();

                attributes.FileAttributes = fs.FileAttributes;
                attributes.GrantedAccess = fs.GrantedAccess;
                attributes.LastAccessTime = fs.LastAccessTime;
                attributes.LastWriteTime = fs.LastWriteTime;
                attributes.AllocationSize = fs.AllocationSize;
                attributes.ChangeTime = fs.ChangeTime;
                attributes.CreationTime = fs.CreationTime;
                attributes.FileSize = fs.FileSize;

                attributes.SecurityDescription = fs.SecurityDescription; // FUCK YOU 
                attributes.SecurityDescription = new byte[0];

                attributes.ExtraBuffer = fs.ExtraBuffer;
                attributes.ReparseData = fs.ReparseData;

                lastFS = fs.Clone<DB.FS>();

                /// Load tree
                if (!lazy)
                    LoadFiles();
            }

            bool loadedFiles = false;

            void LoadFiles()
            {
                if (isDirectory && !loadedFiles)
                {
                    if (lastFS != null)
                    {
                        loadedFiles = true;

                        foreach (var fid in lastFS.Files)
                        {
                            var file = new File(fid, this.vhfs, this);

                            if (file.lastFS != null)
                                files.Set(file.name, file);
                            else
                                Console.WriteLine("ERROR EMPTY FILE LOAD");
                        }
                    }
                }
            }

            // Pretty useless vars
            long lastSave = 0;
            public bool changes = false;

            void Save()
            {        
                if (!changes)
                    return;

                changes = false;

                var fs = new FS();

                if(lastFS != null)
                    fs.AbsIndex = lastFS.AbsIndex;

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
                fs.ExtraBuffer = attributes.ExtraBuffer;

                fs.SecurityDescription = attributes.SecurityDescription;

                fs.ReparseData = attributes.ReparseData;

                if (attributes.SecurityDescription == null || attributes.SecurityDescription.Length == 0)
                    fs.SecurityDescription = lastFS?.SecurityDescription ?? attributes.SecurityDescription;

                // Get file tree
                if (isDirectory)
                {
                    fs.Files = new long[files.Keys.Count];

                    var f = 0;
                    var Keys = new List<string>(files.Keys);
                    foreach (var key in Keys)
                    {
                        var fn = Keys[f];
                        File tfile = null;
                        while ((tfile = files.Get(fn)) == null)
                            Console.WriteLine("ERROR: Just another Tree in the wall");

                        tfile.Save();

                        fs.Files[f++] = tfile.ID;
                    }

                    Static.Debug.Write(new string[] { "SavingDir", name, files.Keys.Count.ToString() });
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

                if (String.IsNullOrEmpty(name) || files == null)
                    return this;

                return files.Get(name);
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

            public int ReadingOps = 0;
            public void Read(IntPtr Buffer, UInt64 Offset, UInt32 Length, out UInt32 PBytesTransferred)
            {
                ReadingOps++;

                var bytes = vhfs.chucks.Read(ID, (long)Offset, Length);
                Marshal.Copy(bytes, 0, Buffer, bytes.Length);

                PBytesTransferred = Length;  

                ReadingOps--;
            }

            public int WritingOps = 0;
            public void Write(IntPtr Buffer,  UInt64 Offset, UInt32 Length, Boolean WriteToEndOfFile, Boolean ConstrainedIo, out UInt32 PBytesTransferred, out Fsp.Interop.FileInfo FileInfo)
            {
                WritingOps++;

                Byte[] Bytes = new byte[Length];
                Marshal.Copy(Buffer, Bytes, 0, Bytes.Length);
               
                vhfs.chucks.Write(ID, (long)Offset, Bytes);

                if (WriteToEndOfFile || attributes.FileSize < Offset + Length)
                    SetSize(Offset + Length);

                PBytesTransferred = (UInt32)Bytes.Length;
                FileInfo = GetFileInfo();

                WritingOps--;
            }

            public void SetSize(UInt64 NewSize, Boolean SetAllocationSize = false)
            {
                vhfs.chucks.Resize(ID, (long)attributes.FileSize, (long)NewSize);
                attributes.FileSize = NewSize;

                changes = true;
            }

            public void SetExtraBuffer(IntPtr Buffer, UInt64 Length)
            {
                if(Length == 0)
                {
                    attributes.ExtraBuffer = new byte[0];
                    return;
                }    

                Byte[] Bytes = new byte[Length];
                Marshal.Copy(Buffer, Bytes, 0, Bytes.Length);
                attributes.ExtraBuffer = Bytes;
            }

            public void GetExtraBuffer(IntPtr Buffer, uint ReqLength, out uint Length)
            {
                Marshal.Copy(attributes.ExtraBuffer, 0, Buffer, (int)ReqLength);
                Length = ReqLength;
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

                // Force parent saving
                if (isDirectory)
                {
                    foreach(var file in files.tree)
                    {
                        file.Value.Save();
                    }
                }
                else
                {
                    parent?.Save();
                }
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
                public byte[] ReparseData;
                public byte[] ExtraBuffer;
            }
        }
    }
}
