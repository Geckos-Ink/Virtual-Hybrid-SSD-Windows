using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class DB
    {
        VHFS vhfs;
        string dir;

        public DB(VHFS vhfs) 
        {
            this.vhfs = vhfs;

            var dataDir = "data/";
            if(!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            dir = dataDir + "dev/";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        void InsertBytes(byte[] bytes)
        {

        }

        class BytesTable
        {
            DB db;

            public BytesTable(DB db, int size)
            {
                this.db = db;
            }
        }

        class Table<T>
        {
            DB db;

            Dictionary<string, MemberInfo> members = new Dictionary<string, MemberInfo>();

            public Table(DB db)
            {
                this.db = db;

                var type = typeof(T);

                var members = type.GetMembers();
                foreach(var member in members)
                {
                    this.members.Add(member.Name, member);
                }
            }

            public void Insert (T row)
            {

            }
        }

        #region InternalStructs

        [Serializable]
        struct DataIndex
        {
            public ulong Index;
            public ushort Size;
        }

        [Serializable]
        struct Value
        {
            public byte[] Bytes;
        }

        [Serializable]
        struct Keys
        {
            public Value[] OrderedKeys;
            public ulong[] OrderedRowsIndexes;
        }

        [Serializable]
        struct Row
        {
            public ulong Index;
        }

        [Serializable]
        struct Values
        {
            public Value[] Data;
        }

        [Serializable]
        struct Table
        {
            public DataIndex[] Columns;
        }

        #endregion

        #region Tables

        [Serializable]
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
