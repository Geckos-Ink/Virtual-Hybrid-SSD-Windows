using Sqlite.Fast;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class DB
    {
        VHFS vhfs;
        Connection conn;

        Statement<FS> stmtFSInsert;

        public DB(VHFS vhfs) 
        {
            this.vhfs = vhfs;

            var dataDir = "data/";
            if(!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);  

            this.conn = new Connection(Path.Combine(dataDir, "db.db"));

            /// Prepare statements
            string sql = "insert into fs values (" +
                "@id," +
                "@parent," +
                "@isDirectory," +
                "@fileAttributes," +
                "@grantedAccess," +
                "@fileSize," +
                "@allocationSize," +
                "@creationTime," +
                "@lastAccessTime," +
                "@lastWriteTime," +
                "@changeTime," +
                "@securityDescription)";

            stmtFSInsert = conn.CompileStatement<FS>(sql);
        }

        #region Tables

        public struct FS
        {
            public ulong ID;
            public ulong Parent;

            public bool IsDirectory;

            // Attributes
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

        #endregion
    }
}
