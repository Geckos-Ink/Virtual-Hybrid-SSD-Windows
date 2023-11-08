/**
 * @file Program.cs
 *
 * @copyright 2015-2022 Bill Zissimopoulos
 * @copyright 2023      Riccardo Cecchini
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

namespace VHSSD
{
    class Ptfs : FileSystemBase
    {
        protected const int ALLOCATION_UNIT = 4096;

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

        public class FileDesc
        {
            public FileStream Stream;
            public DirectoryInfo DirInfo;
            public DictionaryEntry[] FileSystemInfos;

            public FileDesc(FileStream Stream)
            {
                this.Stream = Stream;
            }
            public FileDesc(DirectoryInfo DirInfo)
            {
                this.DirInfo = DirInfo;
            }
            public static void GetFileInfoFromFileSystemInfo(
                FileSystemInfo Info,
                out FileInfo FileInfo)
            {
                FileInfo.FileAttributes = (UInt32)Info.Attributes;
                FileInfo.ReparseTag = 0;
                FileInfo.FileSize = Info is System.IO.FileInfo ?
                    (UInt64)((System.IO.FileInfo)Info).Length : 0;
                FileInfo.AllocationSize = (FileInfo.FileSize + ALLOCATION_UNIT - 1)
                    / ALLOCATION_UNIT * ALLOCATION_UNIT;
                FileInfo.CreationTime = (UInt64)Info.CreationTimeUtc.ToFileTimeUtc();
                FileInfo.LastAccessTime = (UInt64)Info.LastAccessTimeUtc.ToFileTimeUtc();
                FileInfo.LastWriteTime = (UInt64)Info.LastWriteTimeUtc.ToFileTimeUtc();
                FileInfo.ChangeTime = FileInfo.LastWriteTime;
                FileInfo.IndexNumber = 0;
                FileInfo.HardLinks = 0;
            }
            public Int32 GetFileInfo(out FileInfo FileInfo)
            {
                if (null != Stream)
                {
                    BY_HANDLE_FILE_INFORMATION Info;
                    if (!GetFileInformationByHandle(Stream.SafeFileHandle.DangerousGetHandle(),
                        out Info))
                        ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());

                    FileInfo.FileAttributes = Info.dwFileAttributes;
                    FileInfo.ReparseTag = 0;
                    FileInfo.FileSize = (UInt64)Stream.Length;
                    FileInfo.AllocationSize = (FileInfo.FileSize + ALLOCATION_UNIT - 1)
                        / ALLOCATION_UNIT * ALLOCATION_UNIT;
                    FileInfo.CreationTime = Info.ftCreationTime;
                    FileInfo.LastAccessTime = Info.ftLastAccessTime;
                    FileInfo.LastWriteTime = Info.ftLastWriteTime;
                    FileInfo.ChangeTime = FileInfo.LastWriteTime;
                    FileInfo.IndexNumber = 0;
                    FileInfo.HardLinks = 0;
                }
                else
                    GetFileInfoFromFileSystemInfo(DirInfo, out FileInfo);
                return STATUS_SUCCESS;
            }
            public void SetBasicInfo(
                UInt32 FileAttributes,
                UInt64 CreationTime,
                UInt64 LastAccessTime,
                UInt64 LastWriteTime)
            {
                if (0 == FileAttributes)
                    FileAttributes = (UInt32)System.IO.FileAttributes.Normal;
                if (null != Stream)
                {
                    FILE_BASIC_INFO Info = default(FILE_BASIC_INFO);
                    if (unchecked((UInt32)(-1)) != FileAttributes)
                        Info.FileAttributes = FileAttributes;
                    if (0 != CreationTime)
                        Info.CreationTime = CreationTime;
                    if (0 != LastAccessTime)
                        Info.LastAccessTime = LastAccessTime;
                    if (0 != LastWriteTime)
                        Info.LastWriteTime = LastWriteTime;
                    if (!SetFileInformationByHandle(Stream.SafeFileHandle.DangerousGetHandle(),
                        0/*FileBasicInfo*/, ref Info, (UInt32)Marshal.SizeOf(Info)))
                        ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                }
                else
                {
                    if (unchecked((UInt32)(-1)) != FileAttributes)
                        DirInfo.Attributes = (System.IO.FileAttributes)FileAttributes;
                    if (0 != CreationTime)
                        DirInfo.CreationTimeUtc = DateTime.FromFileTimeUtc((Int64)CreationTime);
                    if (0 != LastAccessTime)
                        DirInfo.LastAccessTimeUtc = DateTime.FromFileTimeUtc((Int64)LastAccessTime);
                    if (0 != LastWriteTime)
                        DirInfo.LastWriteTimeUtc = DateTime.FromFileTimeUtc((Int64)LastWriteTime);
                }
            }
            public UInt32 GetFileAttributes()
            {
                FileInfo FileInfo;
                GetFileInfo(out FileInfo);
                return FileInfo.FileAttributes;
            }
            public void SetFileAttributes(UInt32 FileAttributes)
            {
                SetBasicInfo(FileAttributes, 0, 0, 0);
            }
            public Byte[] GetSecurityDescriptor()
            {
                if (null != Stream)
                    return Stream.GetAccessControl().GetSecurityDescriptorBinaryForm();
                else
                    return DirInfo.GetAccessControl().GetSecurityDescriptorBinaryForm();
            }
            public void SetSecurityDescriptor(AccessControlSections Sections, Byte[] SecurityDescriptor)
            {
                Int32 SecurityInformation = 0;
                if (0 != (Sections & AccessControlSections.Owner))
                    SecurityInformation |= 1/*OWNER_SECURITY_INFORMATION*/;
                if (0 != (Sections & AccessControlSections.Group))
                    SecurityInformation |= 2/*GROUP_SECURITY_INFORMATION*/;
                if (0 != (Sections & AccessControlSections.Access))
                    SecurityInformation |= 4/*DACL_SECURITY_INFORMATION*/;
                if (0 != (Sections & AccessControlSections.Audit))
                    SecurityInformation |= 8/*SACL_SECURITY_INFORMATION*/;
                if (null != Stream)
                {
                    if (!SetKernelObjectSecurity(Stream.SafeFileHandle.DangerousGetHandle(),
                        SecurityInformation, SecurityDescriptor))
                        ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                }
                else
                {
                    if (!SetFileSecurityW(DirInfo.FullName,
                        SecurityInformation, SecurityDescriptor))
                        ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                }
            }
            public void SetDisposition(Boolean Safe)
            {
                if (null != Stream)
                {
                    FILE_DISPOSITION_INFO Info;
                    Info.DeleteFile = true;
                    if (!SetFileInformationByHandle(Stream.SafeFileHandle.DangerousGetHandle(),
                        4/*FileDispositionInfo*/, ref Info, (UInt32)Marshal.SizeOf(Info)))
                        if (!Safe)
                            ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
                }
                else
                    try
                    {
                        DirInfo.Delete();
                    }
                    catch (Exception ex)
                    {
                        if (!Safe)
                            ThrowIoExceptionWithHResult(ex.HResult);
                    }
            }
            public static void Rename(String FileName, String NewFileName, Boolean ReplaceIfExists)
            {
                if (!MoveFileExW(FileName, NewFileName, ReplaceIfExists ? 1U/*MOVEFILE_REPLACE_EXISTING*/ : 0))
                    ThrowIoExceptionWithWin32(Marshal.GetLastWin32Error());
            }

            /* interop */
            [StructLayout(LayoutKind.Sequential, Pack = 4)]
            private struct BY_HANDLE_FILE_INFORMATION
            {
                public UInt32 dwFileAttributes;
                public UInt64 ftCreationTime;
                public UInt64 ftLastAccessTime;
                public UInt64 ftLastWriteTime;
                public UInt32 dwVolumeSerialNumber;
                public UInt32 nFileSizeHigh;
                public UInt32 nFileSizeLow;
                public UInt32 nNumberOfLinks;
                public UInt32 nFileIndexHigh;
                public UInt32 nFileIndexLow;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct FILE_BASIC_INFO
            {
                public UInt64 CreationTime;
                public UInt64 LastAccessTime;
                public UInt64 LastWriteTime;
                public UInt64 ChangeTime;
                public UInt32 FileAttributes;
            }
            [StructLayout(LayoutKind.Sequential)]
            private struct FILE_DISPOSITION_INFO
            {
                public Boolean DeleteFile;
            }

            
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern Boolean GetFileInformationByHandle(
                IntPtr hFile,
                out BY_HANDLE_FILE_INFORMATION lpFileInformation);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern Boolean SetFileInformationByHandle(
                IntPtr hFile,
                Int32 FileInformationClass,
                ref FILE_BASIC_INFO lpFileInformation,
                UInt32 dwBufferSize);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern Boolean SetFileInformationByHandle(
                IntPtr hFile,
                Int32 FileInformationClass,
                ref FILE_DISPOSITION_INFO lpFileInformation,
                UInt32 dwBufferSize);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern Boolean MoveFileExW(
                [MarshalAs(UnmanagedType.LPWStr)] String lpExistingFileName,
                [MarshalAs(UnmanagedType.LPWStr)] String lpNewFileName,
                UInt32 dwFlags);
            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern Boolean SetFileSecurityW(
                [MarshalAs(UnmanagedType.LPWStr)] String FileName,
                Int32 SecurityInformation,
                Byte[] SecurityDescriptor);
            [DllImport("advapi32.dll", SetLastError = true)]
            private static extern Boolean SetKernelObjectSecurity(
                IntPtr Handle,
                Int32 SecurityInformation,
                Byte[] SecurityDescriptor);
            
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
            Host.SectorSize = ALLOCATION_UNIT;
            Host.SectorsPerAllocationUnit = 1;
            Host.MaxComponentLength = 255;
            Host.FileInfoTimeout = 1000;
            Host.CaseSensitiveSearch = false;
            Host.CasePreservedNames = true;
            Host.UnicodeOnDisk = true;
            Host.PersistentAcls = true;
            Host.PostCleanupWhenModifiedOnly = true;
            Host.PassQueryDirectoryPattern = true;
            Host.FlushAndPurgeOnCleanup = true;
            Host.VolumeCreationTime = (ulong) DateTime.Now.ToFileTimeUtc(); 
            Host.VolumeSerialNumber = 0;
            return STATUS_SUCCESS;
        }
        public override Int32 GetVolumeInfo(
            out VolumeInfo VolumeInfo)
        {
            VolumeInfo = default(VolumeInfo);

            //todo
            VolumeInfo.TotalSize = (ulong)1024 * 1024 * 1024 * 100;
            VolumeInfo.FreeSize = VolumeInfo.TotalSize;

            return STATUS_SUCCESS;
        }
        public override Int32 GetSecurityByName(
            String FileName,
            out UInt32 FileAttributes/* or ReparsePointIndex */,
            ref Byte[] SecurityDescriptor)
        {
            var file = vhfs.GetFile(FileName);

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
            FileDesc FileDesc = null;
            try
            {
                //FileName = ConcatPath(FileName);

                VHFS.File file;

                if (0 == (CreateOptions & FILE_DIRECTORY_FILE))
                {
                    file = new VHFS.File(true);

                    file.attributes.SecurityDescription = SecurityDescriptor;
                    file.attributes.GrantedAccess = GrantedAccess;
                    file.attributes.FileAttributes = FileAttributes;
                }
                else
                {
                    file = new VHFS.File(false);

                    file.attributes.SecurityDescription = SecurityDescriptor;
                    file.attributes.FileAttributes = FileAttributes;
                }

                vhfs.AddFile(file, FileName);

                FileNode = default(Object);
                FileDesc0 = file;
                NormalizedName = default(String);
                FileInfo = file.GetFileInfo();

                return STATUS_SUCCESS;
            }
            catch
            {
                if (null != FileDesc && null != FileDesc.Stream)
                    FileDesc.Stream.Dispose();
                throw;
            }
        }

        public override Int32 Open(
            String FileName,
            UInt32 CreateOptions,
            UInt32 GrantedAccess,
            out Object FileNode,
            out Object FileDesc0,
            out FileInfo FileInfo,
            out String NormalizedName)
        {

            //FileName = ConcatPath(FileName);
            var file = vhfs.GetFile(FileName);

            try
            {
                FileNode = default(Object);
                FileDesc0 = file;
                NormalizedName = default(String);
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
            FileDesc FileDesc = (FileDesc)FileDesc0;
            if (ReplaceFileAttributes)
                FileDesc.SetFileAttributes(FileAttributes |
                    (UInt32)System.IO.FileAttributes.Archive);
            else if (0 != FileAttributes)
                FileDesc.SetFileAttributes(FileDesc.GetFileAttributes() | FileAttributes |
                    (UInt32)System.IO.FileAttributes.Archive);
            FileDesc.Stream.SetLength(0);
            return FileDesc.GetFileInfo(out FileInfo);
        }

        public override void Cleanup(
            Object FileNode,
            Object FileDesc0,
            String FileName,
            UInt32 Flags)
        {
            var file = (VHFS.File)FileDesc0;

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
                ThrowIoExceptionWithNtStatus(STATUS_END_OF_FILE);

            file.Read(Buffer, Offset, Length, out PBytesTransferred);
       
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

            file.Write(Buffer, Offset, Length, WriteToEndOfFile, ConstrainedIo, out PBytesTransferred, out FileInfo);

            return STATUS_SUCCESS;
              
        }

        public override Int32 Flush(
            Object FileNode,
            Object FileDesc0,
            out FileInfo FileInfo)
        {
            var file = (VHFS.File)FileDesc0;
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

            file.attributes.FileAttributes = FileAttributes;
            file.attributes.CreationTime = CreationTime;
            file.attributes.LastAccessTime = LastAccessTime;
            file.attributes.LastWriteTime = LastWriteTime;
            file.attributes.ChangeTime = ChangeTime;

            FileInfo = file.GetFileInfo();

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
            //FileName = ConcatPath(FileName);
            //NewFileName = ConcatPath(NewFileName);

            vhfs.Rename(FileName, NewFileName, ReplaceIfExists);

            return STATUS_SUCCESS;
        }
        public override Int32 GetSecurity(
            Object FileNode,
            Object FileDesc0,
            ref Byte[] SecurityDescriptor)
        {
            var file = (VHFS.File)FileDesc0;
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

            file.attributes.SecurityDescription = SecurityDescriptor;
            //todo: implement Sections...

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

            var files = dir.ListFiles();

            IEnumerator<String> Enumerator = (IEnumerator<String>)Context;

            if (null == Enumerator)
            {
                List<String> ChildrenFileNames = new List<String>();
                if ("\\" != dir.name)
                {
                    /* if this is not the root directory add the dot entries */
                    if (null == Marker)
                        ChildrenFileNames.Add(".");
                    if (null == Marker || "." == Marker)
                        ChildrenFileNames.Add("..");
                }
                ChildrenFileNames.AddRange(files);
                Context = Enumerator = ChildrenFileNames.GetEnumerator();
            }

            while (Enumerator.MoveNext())
            {
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
                }
            }

            FileName = default(String);
            FileInfo = default(FileInfo);
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
            try
            {
                String DebugLogFile = null;
                UInt32 DebugFlags = 0;
                String VolumePrefix = null;
                String PassThrough = null;
                String MountPoint = null;
                IntPtr DebugLogHandle = (IntPtr)(-1);
                FileSystemHost Host = null;
                Ptfs Ptfs = null;
                int I;

                MountPoint = "X:";
                VolumePrefix = "\\vhfs\\test";

                vhfs = new VHFS();

                vhfs.AddDrive("C", true);
                vhfs.AddDrive("E", false);

                Host = new FileSystemHost(Ptfs = new Ptfs(vhfs));
                Host.Prefix = VolumePrefix;
                if (0 > Host.Mount(MountPoint, null, true, DebugFlags))
                    throw new IOException("cannot mount file system");
                MountPoint = Host.MountPoint();
                _Host = Host;


                Console.WriteLine("Press Enter to close the program...");

                while (Console.ReadKey().Key != ConsoleKey.Enter)
                    Thread.Sleep(1);

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
            _Host.Unmount();
            _Host = null;

            vhfs.Close();
            Console.WriteLine("VHFS Closed.");
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
