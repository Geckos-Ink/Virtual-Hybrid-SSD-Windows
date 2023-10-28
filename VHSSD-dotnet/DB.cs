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

        #region BytesTables 

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

        #endregion

        #region Types

        Dictionary<System.Type, Type> types = new Dictionary<System.Type, Type>();
        Type GetType(System.Type type)
        {
            if (!types.ContainsKey(type))
                types.Add(type, new Type(this, type));

            return types[type];
        }

        public class Type
        {
            DB db;
            System.Type type;

            List<string> membersOrder;
            Dictionary<string, Member> members;

            public int size = -1;

            public bool hasDynamicSize = false;

            public Type(DB db, System.Type type)
            {
                this.db = db;
                this.type = type;

                if (type.IsClass)
                {
                    this.membersOrder = new List<string>();
                    this.members = new Dictionary<string, Member>();

                    size = 0;

                    var members = type.GetMembers();
                    foreach (var member in members)
                    {
                        var m = new Member(db, member);
                        this.members.Add(member.Name, m);
                        this.membersOrder.Add(member.Name);

                        if (m.type.hasDynamicSize)
                            hasDynamicSize = true;

                        size += m.size;
                    }
                }
                else
                {
                    if (!type.IsArray) {
                        if (type.IsValueType)
                            size = Marshal.SizeOf(type);                     
                    }
                    else
                    {
                        hasDynamicSize = true;
                    }
                }
            }

            class Member
            {
                public MemberInfo info;
                public Type type;

                public int size = 0;

                public Member(DB db, MemberInfo info)
                {
                    this.info = info;

                    var type = info.DeclaringType;
                    this.type = db.GetType(type);

                    if (type.IsArray)
                        size = db.GetType(typeof(DataIndex)).size;
                    else
                        size = this.type.size;
                }
            }
        }

        #endregion

        #region OrderedKeys

        public class OrderedKeys
        {
            DB db;

            public OrderedKeys(DB db, string name)
            {
                this.db = db;
            }
        }

        #endregion

        public class Table<T>
        {
            DB db;

            public string ctx = "";
            public Type type;
            public int RowSize = 0;

            public Table(DB db, string ctx="")
            {
                this.db = db;
                this.ctx = ctx;

                type = db.GetType(typeof(T));
                RowSize = type.size;
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
