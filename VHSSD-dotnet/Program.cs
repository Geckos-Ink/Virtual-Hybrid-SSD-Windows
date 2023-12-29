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
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

using Fsp;
using VolumeInfo = Fsp.Interop.VolumeInfo;
using FileInfo = Fsp.Interop.FileInfo;
using System.Collections.Generic;
using System.Threading;
using System.Net.NetworkInformation;
using System.Security.Cryptography.X509Certificates;

namespace VHSSD
{
    class Ptfs : FileSystemBase
    {
        protected static void ThrowIoExceptionWithHResult(Int32 HResult)
        {
            throw new IOException(null, HResult);
        }
        protected static void ThrowIoExceptionWithWin32(Int32 Error)
        {
            ThrowIoExceptionWithHResult(unchecked((Int32)(0x80070000 | Error)));
        }
        protected static void ThrowIoExceptionWithNtStatus(Int32 Status)
        {
            ThrowIoExceptionWithWin32((Int32)Win32FromNtStatus(Status));
        }

        private class DirectoryEntryComparer : IComparer
        {
            public int Compare(object x, object y)
            {
                return String.Compare(
                    (String)((DictionaryEntry)x).Key,
                    (String)((DictionaryEntry)y).Key);
            }
        }
        private static DirectoryEntryComparer _DirectoryEntryComparer =
            new DirectoryEntryComparer();

        VHFS vhfs;
        public Ptfs(VHFS vhfs)
        {
            this.vhfs = vhfs;

            // Temporary
            _Path = "temp";
            if (!Directory.Exists("temp"))
                Directory.CreateDirectory("temp");
        }

        public Ptfs(String Path0)
        {
            _Path = Path.GetFullPath(Path0);
            if (_Path.EndsWith("\\"))
                _Path = _Path.Substring(0, _Path.Length - 1);
        }
        public String ConcatPath(String FileName)
        {
            return _Path + FileName;
        }
        public override Int32 ExceptionHandler(Exception ex)
        {
            Int32 HResult = ex.HResult; /* needs Framework 4.5 */
            if (0x80070000 == (HResult & 0xFFFF0000))
                return NtStatusFromWin32((UInt32)HResult & 0xFFFF);
            return STATUS_UNEXPECTED_IO_ERROR;
        }
        public override Int32 Init(Object Host0)
        {
            FileSystemHost Host = (FileSystemHost)Host0;

            Host.SectorSize = 4096; // should 4096
            Host.SectorsPerAllocationUnit = 1;
            Host.MaxComponentLength = 1023;
            Host.FileInfoTimeout = 1000;
            Host.CaseSensitiveSearch = false;
            Host.CasePreservedNames = true;
            Host.UnicodeOnDisk = false;
            Host.PersistentAcls = false;
            Host.PostCleanupWhenModifiedOnly = true;
            Host.PassQueryDirectoryPattern = false;
            Host.FlushAndPurgeOnCleanup = false;
            Host.ReparsePoints = true;
            Host.ExtendedAttributes = true;
            Host.AllowOpenInKernelMode = true;
            Host.EaTimeout = 1000;
            Host.WslFeatures = false;
            Host.ReparsePointsAccessCheck = true;
            Host.SecurityTimeout = 1000;
            Host.VolumeCreationTime = 1703808432000; //(ulong)Static.UnixTimeMS; //todo: save its creation time 
            Host.VolumeSerialNumber = 1994;

            return STATUS_SUCCESS;
        }
        public override Int32 GetVolumeInfo(
            out VolumeInfo VolumeInfo)
        {
            Static.Debug.Write(new string[] { "GetVolumeInfo" });

            VolumeInfo = default(VolumeInfo);

            //todo
            VolumeInfo.TotalSize = (ulong)vhfs.TotalSize;
            VolumeInfo.FreeSize = (ulong)(vhfs.TotalSize - vhfs.UsedBytes);

            return STATUS_SUCCESS;
        }
        public override Int32 GetSecurityByName(
            String FileName,
            out UInt32 FileAttributes/* or ReparsePointIndex */,
            ref Byte[] SecurityDescriptor)
        {
            var file = vhfs.GetFile(FileName);

            Static.Debug.Write(new string[] { "GetSecurityByName", file?.name ?? "NOT_FOUND: " + FileName });

            if (file == null)
            {
                FileAttributes = 0;
                return STATUS_OBJECT_NAME_NOT_FOUND;
            }

            FileAttributes = file.attributes.FileAttributes;

            SecurityDescriptor = file.attributes.SecurityDescription;

            return STATUS_SUCCESS;
        }

        public override Int32 Create(
            String FileName,
            UInt32 CreateOptions,
            UInt32 GrantedAccess,
            UInt32 FileAttributes,
            Byte[] SecurityDescriptor,
            UInt64 AllocationSize,
            out Object FileNode,
            out Object FileDesc0,
            out FileInfo FileInfo,
            out String NormalizedName)
        {
            var file = vhfs.GetFile(FileName);

            Static.Debug.Write(new string[] { "Create", FileName });

            if (file != null)
            {
                FileNode = default(Object);
                FileDesc0 = file;
                NormalizedName = default(String);
                FileInfo = file.GetFileInfo();
                return STATUS_OBJECT_NAME_COLLISION;
            }

            if (0 == (CreateOptions & FILE_DIRECTORY_FILE))
            {
                file = new VHFS.File(false);
            }
            else
            {
                file = new VHFS.File(true);
            }


            file.attributes.SecurityDescription = SecurityDescriptor;
            file.attributes.GrantedAccess = GrantedAccess;
            file.attributes.FileAttributes = FileAttributes;
            file.attributes.AllocationSize = AllocationSize;

            file.attributes.CreationTime = Static.FileTime;
            file.attributes.ChangeTime = Static.FileTime;
            file.attributes.LastAccessTime = Static.FileTime;
            file.attributes.LastWriteTime = Static.FileTime;

            file.changes = true;
            file.loaded = true;

            vhfs.AddFile(file, FileName);

            FileNode = default(Object);
            FileDesc0 = file;
            NormalizedName = default(String);
            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS; 
        }

        #region Ex

        // Override the GetEaSize method
        public int GetEaSize(
            string FileName,
            out uint EaSize,
            ref FileInfo FileInfo)
        {
            // Initialize the EA size to zero
            EaSize = 0;

            Static.Debug.Write(new string[] { "GetEaSize", FileName });

            try
            {
                var file = vhfs.GetFile(FileName);

                if (file == null)
                    throw new Exception("File not found");

                FileInfo = file.GetFileInfo();
                EaSize = (uint)(file.attributes.ExtraBuffer?.Length ?? 0);
                
                return STATUS_SUCCESS;
            }
            catch (Exception ex)
            {
                // Handle exceptions and return an appropriate NtStatus
                // For example, on a generic error:
                // return NtStatus.STATUS_UNSUCCESSFUL;

                // Log the exception for debugging purposes
                Console.WriteLine("Error in GetEaSize: " + ex.Message);

                return STATUS_OBJECT_NAME_NOT_FOUND;
            }
        }

        public override int CreateEx(string FileName, uint CreateOptions, uint GrantedAccess, uint FileAttributes, byte[] SecurityDescriptor, ulong AllocationSize, IntPtr ExtraBuffer, uint ExtraLength, bool ExtraBufferIsReparsePoint, out object FileNode, out object FileDesc, out FileInfo FileInfo, out string NormalizedName)
        {
            var file = vhfs.GetFile(FileName);

            Static.Debug.Write(new string[] { "CreateEx", FileName });

            if (file != null)
            {
                FileNode = default(Object);
                FileDesc = file;
                NormalizedName = default(String);
                FileInfo = file.GetFileInfo();
                return STATUS_OBJECT_NAME_COLLISION;
            }

            if (0 == (CreateOptions & FILE_DIRECTORY_FILE))
            {
                file = new VHFS.File(false);
            }
            else
            {
                file = new VHFS.File(true);
            }

            file.Ops++;

            file.attributes.SecurityDescription = SecurityDescriptor;
            file.attributes.GrantedAccess = GrantedAccess;
            file.attributes.FileAttributes = FileAttributes;
            file.attributes.AllocationSize = AllocationSize;

            file.attributes.CreationTime = Static.FileTime;
            file.attributes.ChangeTime = Static.FileTime;
            file.attributes.LastAccessTime = Static.FileTime;
            file.attributes.LastWriteTime = Static.FileTime;

            file.SetExtraBuffer(ExtraBuffer, ExtraLength);

            file.changes = true;
            file.loaded = true;

            vhfs.AddFile(file, FileName);

            FileNode = default(Object);
            FileDesc = file;
            NormalizedName = default(String);
            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override int OverwriteEx(object FileNode, object FileDesc, uint FileAttributes, bool ReplaceFileAttributes, ulong AllocationSize, IntPtr Ea, uint EaLength, out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "OverwriteEx", file.name });

            if (ReplaceFileAttributes) //todo: check if this could destroy FileAttributes
                file.attributes.FileAttributes = FileAttributes;

            //todo: Check AllocationSize behaviour
            file.attributes.AllocationSize = AllocationSize;
            file.attributes.ChangeTime = Static.FileTime;
            file.attributes.LastAccessTime = Static.FileTime;

            file.SetExtraBuffer(Ea, EaLength);

            file.changes = true;

            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override int GetEa(object FileNode, object FileDesc, IntPtr Ea, uint EaLength, out uint BytesTransferred)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "GetEa", file.name });

            file.GetExtraBuffer(Ea, EaLength, out BytesTransferred);

            return STATUS_SUCCESS;
        }

        public override int SetEa(object FileNode, object FileDesc, IntPtr Ea, uint EaLength, out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "SetEa", file.name });

            file.SetExtraBuffer(Ea, EaLength);

            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override bool GetEaEntry(object FileNode, object FileDesc, ref object Context, out string EaName, out byte[] EaValue, out bool NeedEa)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "GetEaEntry", file.name });

            return base.GetEaEntry(FileNode, FileDesc, ref Context, out EaName, out EaValue, out NeedEa);
        }

        public override int SetEaEntry(object FileNode, object FileDesc, ref object Context, string EaName, byte[] EaValue, bool NeedEa)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "SetEaEntry", file.name, EaName });

            return base.SetEaEntry(FileNode, FileDesc, ref Context, EaName, EaValue, NeedEa);
        }

        #endregion

        public override Int32 Open(
            String FileName,
            UInt32 CreateOptions,
            UInt32 GrantedAccess,
            out Object FileNode,
            out Object FileDesc0,
            out FileInfo FileInfo,
            out String NormalizedName)
        {

            var file = vhfs.GetFile(FileName);

            file.Ops++;

            Static.Debug.Write(new string[] { "Open", FileName });

            if (file == null)
            {
                FileNode = default(Object);
                FileDesc0 = default(Object);
                FileInfo = default(FileInfo);
                NormalizedName = default(String);

                return STATUS_OBJECT_NAME_NOT_FOUND;
            }

            file.attributes.LastAccessTime = Static.FileTime;

            try
            {
                file.Load(false);

                FileNode = file;
                FileDesc0 = file;
                NormalizedName = default(string);
                FileInfo = file.GetFileInfo();
                return STATUS_SUCCESS;
            }
            catch
            {
                if (null != file && !file.open)
                    file.Dispose();
                throw;
            }
        }
        public override Int32 Overwrite(
            Object FileNode,
            Object FileDesc0,
            UInt32 FileAttributes,
            Boolean ReplaceFileAttributes,
            UInt64 AllocationSize,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "Overwrite", file.name });

            if (ReplaceFileAttributes) //todo: check if this could destroy FileAttributes
                file.attributes.FileAttributes = FileAttributes;

            //todo: Check AllocationSize behaviour
            file.attributes.AllocationSize = AllocationSize;
            file.attributes.ChangeTime = Static.FileTime;
            file.attributes.LastAccessTime = Static.FileTime;

            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override void Cleanup(
            Object FileNode,
            Object FileDesc0,
            String FileName,
            UInt32 Flags)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "Cleanup", file.name });

            if ((Flags & CleanupDelete) != 0)
            {
                file.Remove();   
            }
        }

        public override void Close(
            Object FileNode,
            Object FileDesc0)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "Close", file.name });

            file.Ops--;

            if(file.Ops == 0)
                file.Dispose();
        }

        public override Int32 Read(
            Object FileNode,
            Object FileDesc0,
            IntPtr Buffer,
            UInt64 Offset,
            UInt32 Length,
            out UInt32 PBytesTransferred)
        {
            var file = (VHFS.File)FileDesc0;

            if (Offset > (UInt64)file.attributes.FileSize)
            {
                PBytesTransferred = 0;
                return STATUS_END_OF_FILE;
            }

            file.attributes.LastAccessTime = Static.FileTime;

            file.Read(Buffer, Offset, Length, out PBytesTransferred);

            Static.Debug.Write(new string[] { "Read", file.name, "Offset:", Offset.ToString(), "Length:", Length.ToString() });

            //Console.WriteLine("Read: \tOffset: "+ Offset + "\tLength: "+Length+ "\tTransferred: "+ PBytesTransferred + "\tAllocationDiffer: " + (Offset % 4096));

            /* TODO:
             * Investigate: a file doesn't works? Load it slowly. Why?
            if (file.name == "Crash Bandicoot (USA).iso")
                Thread.Sleep(1);
            */

            return STATUS_SUCCESS;
        }

        public override Int32 Write(
            Object FileNode,
            Object FileDesc0,
            IntPtr Buffer,
            UInt64 Offset,
            UInt32 Length,
            Boolean WriteToEndOfFile,
            Boolean ConstrainedIo,
            out UInt32 PBytesTransferred,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "Write", file.name, "Offset:", Offset.ToString(), "Length:", Length.ToString() });

            file.attributes.ChangeTime = Static.FileTime;
            file.attributes.LastAccessTime = Static.FileTime;
            file.attributes.LastWriteTime = Static.FileTime;

            file.Write(Buffer, Offset, Length, WriteToEndOfFile, ConstrainedIo, out PBytesTransferred, out FileInfo);

            return STATUS_SUCCESS;
              
        }

        public override Int32 Flush(
            Object FileNode,
            Object FileDesc0,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "Flush", file.name });

            file.Flush();
            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override Int32 GetFileInfo(
            Object FileNode,
            Object FileDesc0,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "GetFileInfo", file.name });

            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override Int32 SetBasicInfo(
            Object FileNode,
            Object FileDesc0,
            UInt32 FileAttributes,
            UInt64 CreationTime,
            UInt64 LastAccessTime,
            UInt64 LastWriteTime,
            UInt64 ChangeTime,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "SetBasicInfo", file.name });

            if (unchecked((UInt32)(-1)) != FileAttributes)
                file.attributes.FileAttributes = FileAttributes;
            if (0 != CreationTime)
                file.attributes.CreationTime = CreationTime;
            if (0 != LastAccessTime)
                file.attributes.LastAccessTime = LastAccessTime;
            if (0 != LastWriteTime)
                file.attributes.LastWriteTime = LastWriteTime;
            if (0 != ChangeTime)
                file.attributes.ChangeTime = ChangeTime;

            file.changes = true;

            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override int SetReparsePoint(object FileNode, object FileDesc, string FileName, byte[] ReparseData)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "SetReparsePoint", file.name });

            file.attributes.ReparseData = ReparseData;

            file.changes = true;

            return STATUS_SUCCESS;
        }

        public override int GetReparsePoint(object FileNode, object FileDesc, string FileName, ref byte[] ReparseData)
        {
            var file = (VHFS.File)FileDesc;

            Static.Debug.Write(new string[] { "GetReparsePoint", file.name });

            ReparseData = file.attributes.ReparseData;

            return STATUS_SUCCESS;
        }

        public override Int32 SetFileSize(
            Object FileNode,
            Object FileDesc0,
            UInt64 NewSize,
            Boolean SetAllocationSize,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "SetFileSize", file.name });

            file.SetSize(NewSize);
            FileInfo = file.GetFileInfo();

            return STATUS_SUCCESS;
        }

        public override Int32 CanDelete(
            Object FileNode,
            Object FileDesc0,
            String FileName)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "CanDelete", file.name });

            if (file.isDirectory && file.files.Keys.Count > 0)
                return STATUS_DIRECTORY_NOT_EMPTY;

            return STATUS_SUCCESS;
        }

        public override Int32 Rename(
            Object FileNode,
            Object FileDesc0,
            String FileName,
            String NewFileName,
            Boolean ReplaceIfExists)
        {
            Static.Debug.Write(new string[] { "Rename", FileName, NewFileName, "ReplaceIfExists:", ReplaceIfExists.ToString() });

            vhfs.Rename(FileName, NewFileName, ReplaceIfExists);

            return STATUS_SUCCESS;
        }
        public override Int32 GetSecurity(
            Object FileNode,
            Object FileDesc0,
            ref Byte[] SecurityDescriptor)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "GetSecurity", file.name });

            SecurityDescriptor = file.attributes.SecurityDescription;

            return STATUS_SUCCESS;
        }
        public override Int32 SetSecurity(
            Object FileNode,
            Object FileDesc0,
            AccessControlSections Sections,
            Byte[] SecurityDescriptor)
        {
            var file = (VHFS.File)FileDesc0;

            Static.Debug.Write(new string[] { "SetSecurity", file.name });

            file.attributes.SecurityDescription = SecurityDescriptor;
            //todo: implement Sections...

            file.changes = true;

            return STATUS_SUCCESS;
        }

        public override Boolean ReadDirectoryEntry(
            Object FileNode,
            Object FileDesc0,
            String Pattern,
            String Marker,
            ref Object Context,
            out String FileName,
            out FileInfo FileInfo)
        {
            var dir = (VHFS.File) FileDesc0;

            if (!dir.isDirectory)
            {
                FileName = dir.name;
                FileInfo = dir.GetFileInfo();
                return false;
            }

            Static.Debug.Write(new string[] { "ReadDirectoryEntry", dir.name });

            IEnumerator<String> Enumerator = (IEnumerator<String>)Context;

            if (null == Enumerator)
            {
                var files = dir.ListFiles();

                List<String> ChildrenFileNames = new List<String>();
                if (dir.parent != null)
                {
                    /* if this is not the root directory add the dot entries */
                    if (null == Marker)
                        ChildrenFileNames.Add(".");
                    if (null == Marker || "." == Marker)
                        ChildrenFileNames.Add("..");
                }

                if (Marker != null)
                {
                    if (dir.files.tree.ContainsKey(Marker.ToLower()))
                        ChildrenFileNames.Add(Marker);
                }
                else
                {
                    ChildrenFileNames.AddRange(files);
                }

                Context = Enumerator = ChildrenFileNames.GetEnumerator();
            }

            while (Enumerator.MoveNext())
            {
                Context = Enumerator;
                String FullFileName = Enumerator.Current;
                if ("." == FullFileName)
                {
                    FileName = ".";
                    FileInfo = dir.GetFileInfo();
                    return true;
                }
                else if (".." == FullFileName)
                {
                    var parent = dir.parent;
                    if (parent != null)
                    {
                        FileName = "..";
                        FileInfo = parent.GetFileInfo();
                        return true;
                    }
                }
                else
                {
                    FileName = Path.GetFileName(FullFileName);
                    var file = dir.GetFile(FileName);
                    if (file != null)
                    {
                        FileInfo = file.GetFileInfo();
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("DEBUG!!");
                    }
                }
            }

            FileName = default(String);
            FileInfo = default(FileInfo);
            Context = Enumerator;
            return false;

        }

        private String _Path;
    }

    class PtfsService : Service
    {
        private class CommandLineUsageException : Exception
        {
            public CommandLineUsageException(String Message = null) : base(Message)
            {
                HasMessage = null != Message;
            }

            public bool HasMessage;
        }

        private const String PROGNAME = "passthrough-dotnet";

        public PtfsService() : base("PtfsService")
        {
        }

        VHFS vhfs;

        protected override void OnStart(String[] Args)
        {
            Console.WriteLine("Press r to reset:");
            var reset = Console.ReadKey();
            if (reset.KeyChar == 'r')
                Static.DebugResetEnv = true;

            var settings = new INI("drive.ini");
            Console.WriteLine("drive.ini loaded");

            var letter = settings.Props["letter"] ?? "X";
            var name = settings.Props["name"] ?? "VHSSD";

            vhfs = new VHFS();  

            var ssd = settings.Props.Get("SSD");
            var hdd = settings.Props.Get("HDD");

            void readDriveSettings(INI.Properties dprops, bool isSSD)
            {
                var dletter = dprops["letter"];
                var drive = vhfs.AddDrive(dletter, isSSD);

                var sMaxSize = dprops["maxSize"];
                if (sMaxSize != null)
                {
                    var maxSize = long.Parse(sMaxSize);
                    maxSize *= (long)Math.Pow(1024, 3);
                    drive.MaxSize = maxSize;
                }

                if (!isSSD)
                    vhfs.TotalSize += drive.MaxSize;
            }

            for(int i=0; i<ssd.Count; i++)
            {
                var dprops = ssd.Get(i.ToString());
                readDriveSettings(dprops, true);
            }

            for (int i = 0; i < hdd.Count; i++)
            {
                var dprops = hdd.Get(i.ToString());
                readDriveSettings(dprops, false);
            }

            /*if (vhfs.NewFS || true)
            {
                var cfs = System.IO.File.GetAccessControl("C:/");
                vhfs.root.attributes.SecurityDescription = cfs.GetSecurityDescriptorBinaryForm();
            }*/

            try
            {
                bool Syncronize = true;
                String DebugLogFile = null;
                UInt32 DebugFlags = 0;
                String VolumePrefix = null;
                String PassThrough = null;
                String MountPoint = null;
                IntPtr DebugLogHandle = (IntPtr)(-1);
                FileSystemHost Host = null;
                Ptfs Ptfs = null;
                int I;  

                MountPoint = letter+":";
                VolumePrefix = "\\vhfs\\"+name;

                Host = new FileSystemHost(Ptfs = new Ptfs(vhfs));
                Host.Prefix = VolumePrefix;

                if (0 > Host.MountEx(MountPoint, 0, vhfs.root.attributes.SecurityDescription, Syncronize, DebugFlags))
                    throw new IOException("cannot mount file system");

                MountPoint = Host.MountPoint();
                _Host = Host;

                Console.WriteLine("Press Enter to close the program...");

                while (Console.ReadKey().Key != ConsoleKey.Enter)
                    Thread.Sleep(1);

                Host.Unmount();

                vhfs.Close();
                this.Stop();

                return;

                for (I = 1; Args.Length > I; I++)
                {
                    String Arg = Args[I];
                    if ('-' != Arg[0])
                        break;
                    switch (Arg[1])
                    {
                    case '?':
                        throw new CommandLineUsageException();
                    case 'd':
                        argtol(Args, ref I, ref DebugFlags);
                        break;
                    case 'D':
                        argtos(Args, ref I, ref DebugLogFile);
                        break;
                    case 'm':
                        argtos(Args, ref I, ref MountPoint);
                        break;
                    case 'p':
                        argtos(Args, ref I, ref PassThrough);
                        break;
                    case 'u':
                        argtos(Args, ref I, ref VolumePrefix);
                        break;
                    default:
                        throw new CommandLineUsageException();
                    }
                }

                if (Args.Length > I)
                    throw new CommandLineUsageException();

                if (null == PassThrough && null != VolumePrefix)
                {
                    I = VolumePrefix.IndexOf('\\');
                    if (-1 != I && VolumePrefix.Length > I && '\\' != VolumePrefix[I + 1])
                    {
                        I = VolumePrefix.IndexOf('\\', I + 1);
                        if (-1 != I &&
                            VolumePrefix.Length > I + 1 &&
                            (
                            ('A' <= VolumePrefix[I + 1] && VolumePrefix[I + 1] <= 'Z') ||
                            ('a' <= VolumePrefix[I + 1] && VolumePrefix[I + 1] <= 'z')
                            ) &&
                            '$' == VolumePrefix[I + 2])
                        {
                            PassThrough = String.Format("{0}:{1}", VolumePrefix[I + 1], VolumePrefix.Substring(I + 3));
                        }
                    }
                }

                if (null == PassThrough || null == MountPoint)
                    throw new CommandLineUsageException();

                if (null != DebugLogFile)
                    if (0 > FileSystemHost.SetDebugLogFile(DebugLogFile))
                        throw new CommandLineUsageException("cannot open debug log file");

                Host = new FileSystemHost(Ptfs = new Ptfs(vhfs));
                Host.Prefix = VolumePrefix;
                if (0 > Host.Mount(MountPoint, null, true, DebugFlags))
                    throw new IOException("cannot mount file system");
                MountPoint = Host.MountPoint();
                _Host = Host;

                Log(EVENTLOG_INFORMATION_TYPE, String.Format("{0}{1}{2} -p {3} -m {4}",
                    PROGNAME,
                    null != VolumePrefix && 0 < VolumePrefix.Length ? " -u " : "",
                        null != VolumePrefix && 0 < VolumePrefix.Length ? VolumePrefix : "",
                    PassThrough,
                    MountPoint));
            }
            catch (CommandLineUsageException ex)
            {
                Log(EVENTLOG_ERROR_TYPE, String.Format(
                    "{0}" +
                    "usage: {1} OPTIONS\n" +
                    "\n" +
                    "options:\n" +
                    "    -d DebugFlags       [-1: enable all debug logs]\n" +
                    "    -D DebugLogFile     [file path; use - for stderr]\n" +
                    "    -u \\Server\\Share    [UNC prefix (single backslash)]\n" +
                    "    -p Directory        [directory to expose as pass through file system]\n" +
                    "    -m MountPoint       [X:|*|directory]\n",
                    ex.HasMessage ? ex.Message + "\n" : "",
                    PROGNAME));
                throw;
            }
            catch (Exception ex)
            {
                Log(EVENTLOG_ERROR_TYPE, String.Format("{0}", ex.Message));
                throw;
            }
        }
        protected override void OnStop()
        {
            if (_Host == null) return;

            _Host.Unmount();
            _Host = null;

            vhfs.Close();
            Console.WriteLine("VHFS Closed.");

            System.Environment.Exit(0);
        }

        private static void argtos(String[] Args, ref int I, ref String V)
        {
            if (Args.Length > ++I)
                V = Args[I];
            else
                throw new CommandLineUsageException();
        }
        private static void argtol(String[] Args, ref int I, ref UInt32 V)
        {
            Int32 R;
            if (Args.Length > ++I)
                V = Int32.TryParse(Args[I], out R) ? (UInt32)R : V;
            else
                throw new CommandLineUsageException();
        }

        private FileSystemHost _Host;
    }

    class Program
    {
        static void Main(string[] args)
        {
            Environment.ExitCode = new PtfsService().Run();
        }
    }
}
