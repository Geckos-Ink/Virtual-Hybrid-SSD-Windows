using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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

        Dictionary<int, BytesTable> bytesTables = new Dictionary<int, BytesTable>();
        BytesTable GetBytesTable(int size)
        {
            if(!bytesTables.ContainsKey(size))
                bytesTables.Add(size, new BytesTable(this, size));

            return bytesTables[size];
        }

        class BytesTable
        {
            DB db;
            long size;

            List<long> freeSlots = new List<long>();
            bool freeSlotsChanged = false;

            File fileValues;
            File fileFreeSlots;

            public BytesTable(DB db, int size)
            {
                this.db = db;
                this.size = (long)size;

                fileValues = new File(db.dir + "bt-" + size + ".bin");
                fileFreeSlots = new File(db.dir + "bt-fs-" + size + ".bin");
            }

            public byte[] Get(long index)
            {
                return fileValues.Read(size, size * index);
            }

            public long Set(byte[] value, long index = -1)
            {
                if (index == -1)
                {
                    if(freeSlots.Count > 0)
                    {
                        index = freeSlots[0];
                        freeSlots.RemoveAt(0);
                        freeSlotsChanged = true;
                    }
                    else 
                        index = Length;
                }

                fileValues.Write(value, index * size);

                return index;
            }

            public void Delete(long index)
            {
                freeSlots.Add(index);
                freeSlotsChanged = true;
            }

            long Length
            {
                get
                {
                    return fileValues.Length / size;
                }
            }
        }

        public class Table<T>
        {
            DB db;

            List<string> membersOrder = new List<string>();
            Dictionary<string, Member> members = new Dictionary<string, Member>();

            public Table(DB db)
            {
                this.db = db;

                var type = typeof(T);

                var members = type.GetMembers();
                foreach(var member in members)
                {
                    this.members.Add(member.Name, new Member(member));
                    this.membersOrder.Add(member.Name);
                }
            }

            public void Insert (T row)
            {

            }

            class Member
            {
                MemberInfo info;
                int size = -1;

                public Member(MemberInfo info)
                {
                    this.info = info;

                    var type = info.DeclaringType;
                    if (!type.IsArray)
                    {
                        size = Marshal.SizeOf(type);
                    }
                }
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
