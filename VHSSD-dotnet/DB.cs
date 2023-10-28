using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Runtime.CompilerServices;
using System.CodeDom;

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
            ListStream<long> freeSlotsStream;

            File fileValues;
            File fileFreeSlots;

            public BytesTable(DB db, int size)
            {
                this.db = db;
                this.size = (long)size;

                this.freeSlotsStream = new ListStream<long>(db, "bt-fs-" + size, freeSlots);

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
                        freeSlotsStream.changed = true;
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
                freeSlotsStream.changed = true;
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

            OrderedDictionary<string, Member> members;

            public string name;
            public int size = -1;

            public bool hasDynamicSize = false;

            public bool iterate = false;
            public Type iterateType;

            public Type(DB db, System.Type type)
            {
                this.db = db;
                this.type = type;

                name = type.Name;

                if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
                {
                    iterate = true;

                    // Get generic type argument(s)
                    System.Type genericArgument = type.GetInterfaces()
                                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                                    .Select(i => i.GetGenericArguments()[0])
                                    .FirstOrDefault();

                    iterateType = db.GetType(genericArgument);
                }
                else if (type.IsClass)
                {
                    this.members = new OrderedDictionary<string, Member>();

                    size = 0;

                    var members = type.GetFields();
                    foreach (var member in members)
                    {
                        var m = new Member(db, member);
                        this.members.Add(member.Name, m);

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

            public byte[] ObjToByte (object obj)
            {
                if (type.IsValueType)
                {
                    using (MemoryStream memoryStream = new MemoryStream())
                    {
                        IFormatter formatter = new BinaryFormatter();
                        formatter.Serialize(memoryStream, obj);
                        return memoryStream.ToArray();
                    }
                }
                else
                {
                    if (type.IsArray)
                    {
                        //todo
                        throw new NotSupportedException();
                    }
                    else
                    {
                        List<byte> bytes = new List<byte>();        

                        foreach(var member in members.Items)
                        {
                            var val = member.Value.Extract(obj);
                            var valBytes = member.Value.type.ObjToByte(val);
                            bytes.AddRange(valBytes);
                        }
                    }
                }

                return null;
            }

            public object BytesToObject(byte[] bytes)
            {
                if(type.IsValueType)
                {
                    if (type == typeof(bool))
                        return BitConverter.ToBoolean(bytes, 0);

                    if (type == typeof(short))
                        return BitConverter.ToInt16(bytes, 0);

                    if (type == typeof(ushort))
                        return BitConverter.ToUInt16(bytes, 0);

                    if (type == typeof(int))
                        return BitConverter.ToInt32(bytes, 0);

                    if (type == typeof(uint))
                        return BitConverter.ToUInt32(bytes, 0);

                    if (type == typeof(long))
                        return BitConverter.ToInt64(bytes, 0);

                    if (type == typeof(ulong))
                        return BitConverter.ToUInt64(bytes, 0);

                    if (type == typeof(float))
                        return BitConverter.ToSingle(bytes, 0);

                    if (type == typeof(double))
                        return BitConverter.ToDouble(bytes, 0);

                    if (type == typeof(string))
                        return BitConverter.ToString(bytes, 0);
                }
                else
                {
                    if (type.IsArray)
                    {
                        //todo with dynamic management
                        throw new NotImplementedException();

                        if (type == typeof(byte[]))
                            return bytes;

                        var arrayOf = db.GetType(type.GetElementType());
                        List<object> list = new List<object>();
                        for(int i = 0; i<bytes.Length; i++)
                        {
                            var itemData = bytes.Skip(i).Take(arrayOf.size).ToArray();
                        }
                    }
                    else
                    {
                        object obj = Activator.CreateInstance(type);

                        int i = 0;
                        foreach (var member in members.Items)
                        {
                            var memberData = bytes.Skip(i).Take(member.Value.size).ToArray();
                            var val = member.Value.type.BytesToObject(memberData);
                            member.Value.Set(obj, val);
  
                            i += member.Value.size;
                        }

                        return obj;
                    }
                }

                return null;
            }

            class Member
            {
                public FieldInfo info;
                public Type type;

                public int size = 0;

                public Member(DB db, FieldInfo info)
                {
                    this.info = info;

                    var type = info.DeclaringType;
                    this.type = db.GetType(type);

                    if (type.IsArray)
                        size = db.GetType(typeof(DataIndex)).size;
                    else
                        size = this.type.size;
                }

                public object Extract(object obj)
                {
                    return info.GetValue(obj);
                }

                public void Set(object obj, object val)
                {
                    info.SetValue(obj, val);
                }
            }
        }

        #endregion


        #region OrderedKeys

        public class OrderedKeys<T>
        {
            DB db;
            Type type;

            public File file;
            public string name;

            public OrderedDictionary<T, long> keys = new OrderedDictionary<T, long>(); 

            public OrderedKeys(DB db, string name)
            {
                this.db = db;
                this.name = name;

                this.type = db.GetType(typeof(T));
            }

            public void SetKey(T key, long id)
            {
                if (keys.Has(key))
                    keys[key] = id;
                else 
                    keys.Add(key, id);
            }

            public long GetKey(T key)
            {
                return keys[key];
            }
        }

        #endregion

        #region IterableStreams

        public class ListStream<T>
        {
            DB db;
            List<T> list;

            File file;
            Type type;
            Type getSetType;

            public bool changed;

            public ListStream(DB db, string name, List<T> list)
            {
                this.db = db;
                this.list = list;

                type = db.GetType(list.GetType());
                getSetType = db.GetType(typeof(T));

                file = new File(db.dir + "list-" + name);

                if (file.Length > 0)
                    Load();
            }

            public void Load()
            {
                if (list.Count > 0)
                    return;

                int size = getSetType.size;
                int numRow = (int)file.Length / size;

                var data = file.Read();

                int pos = 0;
                for(long r = 0; r < numRow; r++)
                {
                    var rowData = data.Skip(pos).Take(size).ToArray();
                    var obj = getSetType.BytesToObject(rowData);
                    list.Add((T)obj);

                    pos += size;              
                }
            }

            public void Save()
            {
                var res = new List<byte>();

                foreach(T obj in list)
                {
                    var bytes = getSetType.ObjToByte(obj);
                    res.AddRange(res);
                }

                file.Write(res.ToArray());
                file.Flush();
            }
        }

        #endregion

        public class Table<T>
        {
            DB db;

            public string ctx = "";
            public Type type;
            public int RowSize = -1;

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
