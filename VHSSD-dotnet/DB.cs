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
                if (value.Length != size)
                    throw new ArgumentException("bytes size dismatch with the BytesTable size");

                if (index == -1)
                {
                    if(freeSlots.Count > 0)
                    {
                        index = freeSlots[0];
                        freeSlots.RemoveAt(0);
                        freeSlotsStream.Changed = true;
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
                freeSlotsStream.Changed = true;
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
            public System.Type type;

            public OrderedDictionary<string, Member> members;

            public string name;
            public int size = -1;

            public bool hasDynamicSize = false;

            //public bool iterate = false;
            //public Type iterateType;

            public Type(DB db, System.Type type)
            {
                this.db = db;
                this.type = type;

                name = type.Name;

                /*if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
                {
                    iterate = true;

                    // Get generic type argument(s)
                    System.Type genericArgument = type.GetInterfaces()
                                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                                    .Select(i => i.GetGenericArguments()[0])
                                    .FirstOrDefault();

                    iterateType = db.GetType(genericArgument);
                }*/
                
                if (type.IsClass)
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

            public byte[] ObjToBytes (object obj)
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
                        List<byte> resBytes = new List<byte>();

                        var arrayOf = db.GetType(type.GetElementType());

                        var arr = obj as object[];
                        foreach(var item in arr)
                        {
                            var bytes = arrayOf.ObjToBytes(item);
                            resBytes.AddRange(bytes);
                        }

                        var index = new DataIndex();
                        index.Size = resBytes.Count;
                        index.Index = db.GetBytesTable(index.Size).Set(resBytes.ToArray());

                        var tDataIndex = db.GetType(typeof(DataIndex));
                        return tDataIndex.ObjToBytes(index);
                    }
                    else
                    {
                        List<byte> bytes = new List<byte>();        

                        foreach(var member in members.Items)
                        {
                            var val = member.Value.Extract(obj);
                            var valBytes = member.Value.type.ObjToBytes(val);
                            bytes.AddRange(valBytes);
                        }

                        return bytes.ToArray();
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
                        // Retrieve DataIndex
                        var tDataIndex = db.GetType(typeof(DataIndex));
                        var index = (DataIndex) tDataIndex.BytesToObject(bytes);

                        // Get bytes
                        var allBytes = db.GetBytesTable(index.Size).Get(index.Index);

                        if (type == typeof(byte[]))
                            return allBytes;

                        var arrayOf = db.GetType(type.GetElementType());
                        List<object> list = new List<object>();
                        for(int i = 0; i< allBytes.Length; i++)
                        {
                            var itemData = allBytes.Skip(i).Take(arrayOf.size).ToArray();
                            var obj = arrayOf.BytesToObject(itemData);
                            list.Add(obj);
                        }

                        return list.ToArray();
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

            public class Member
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

            public string name;

            public OrderedDictionary<T, long> keys = new OrderedDictionary<T, long>(); 
            public OrderedDictionaryStream<T, long> stream;

            public OrderedKeys(DB db, string name)
            {
                this.db = db;
                this.name = name;

                stream = new OrderedDictionaryStream<T, long>(db, name, keys);
            }

            public void Set(T key, long id)
            {
                if (keys.Has(key))
                    keys[key] = id;
                else 
                    keys.Add(key, id);

                stream.Changed = true;
            }

            public long Get(T key)
            {
                stream.Changed = stream.Changed; // warn of the usage
                return keys[key];
            }

            public void Delete(T key)
            {
                keys.Remove(key);
            }

            public bool Has(T key)
            {
                return keys.Has(key);
            }

            public void Die()
            {
                stream.file.Delete();
            }
        }

        #endregion

        #region IterableStreams
        
        public abstract class IterateStream
        {
            internal DB db;
            internal File file;

            public long lastChange = 0;
            bool changed = false;
            public bool Changed
            {
                get { return changed; }

                set {
                    lastChange = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    changed = value; 
                }
            }
            IEnumerable iterate;

            internal void InitSaveChecker()
            {
                //todo
            }

            public abstract void Save();
        }

        public class ListStream<T> : IterateStream
        {
            List<T> list;
   
            Type getSetType;

            public bool sort = false;

            public ListStream(DB db, string name, List<T> list)
            {
                this.db = db;
                this.list = list;

                getSetType = db.GetType(typeof(T));

                this.InitSaveChecker();

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

            public override void Save()
            {
                if(sort)
                    list.Sort();

                var res = new List<byte>();

                foreach(T obj in list)
                {
                    var bytes = getSetType.ObjToBytes(obj);
                    res.AddRange(bytes);
                }

                file.Write(res.ToArray());
                file.Flush();
            }
        }

        public class OrderedDictionaryStream<T,V> : IterateStream
        {
            OrderedDictionary<T, V> dict;

            Type keyValueType;

            struct KeyValue<TT, VV>
            {
                public TT Key;
                public VV Value;
            }

            public OrderedDictionaryStream(DB db, string name, OrderedDictionary<T, V> dict)
            {
                this.db = db;
                this.dict = dict;

                keyValueType = db.GetType(typeof(KeyValue<T,V>));

                this.InitSaveChecker();

                file = new File(db.dir + "odict-" + name);

                if (file.Length > 0)
                    Load();
            }

            public void Load()
            {
                if (dict.Items.Count() > 0)
                    return;

                int size = keyValueType.size;
                int numRow = (int)file.Length / size;

                var data = file.Read();

                int pos = 0;
                for (long r = 0; r < numRow; r++)
                {
                    var rowData = data.Skip(pos).Take(size).ToArray();
                    var kv = (KeyValue<T,V>)keyValueType.BytesToObject(rowData);

                    dict.Add(kv.Key, kv.Value);

                    pos += size;
                }
            }

            public override void Save()
            {
                var res = new List<byte>();

                foreach (var obj in dict.Items)
                {       
                    var kv = new KeyValue<T, V>();
                    kv.Key = obj.Key;
                    kv.Value = obj.Value;

                    var bytes = keyValueType.ObjToBytes(kv);

                    res.AddRange(bytes);
                }

                file.Write(res.ToArray());
                file.Flush();
            }
        }

        #endregion

        public Dictionary<string, object> tables = new Dictionary<string, object>();
        public Table<T> GetTable<T>(string ctx = "")
        {
            var t = GetType(typeof(T));
            var name = t.name + (String.IsNullOrEmpty(ctx) ? "" : "-" + ctx);

            object table;
            if(!tables.TryGetValue(name, out table))
            {
                table = new Table<T>(this, name);
                tables.Add(name, table);
            }

            return table as Table<T>;
        }

        public class Table<T>
        {
            DB db;

            public string ctx = "";
            public Type type;
            public int RowSize = -1;

            public List<Key> Keys = new List<Key>();

            BytesTable bytesTable;

            public Table(DB db, string ctx="")
            {
                this.db = db;
                this.ctx = type.name + (String.IsNullOrEmpty(ctx) ? "" : "-" + ctx);

                type = db.GetType(typeof(T));

                RowSize = type.size;
                bytesTable = db.GetBytesTable(RowSize);
            }

            public void Close()
            {
                foreach (var key in Keys)
                {
                    key.Close();
                }
            }

            #region SetKeys

            public void SetKey(string key1, string key2 = null, string key3 = null)
            {
                List<string> keys = new List<string> { key1 };
                if (key2 != null) keys.Add(key2);
                if (key3 != null) keys.Add(key3);

                var manager = new Key(this, keys.ToArray());
                Keys.Add(manager);
            }

            public class Key
            {
                public string[] Relation;
                public OrderedKeys<long> Keys;

                public string name;
                public string prefix;
                Table<T> table;

                public Key (Table<T> table, string[] relation)
                {
                    this.table = table;

                    Relation = relation;

                    name = string.Join(",", relation);
                    
                    prefix = table.ctx + "-" + string.Join("-", relation);
                    Keys = new OrderedKeys<long>(table.db, prefix);
                }

                public void Close()
                {
                    Keys.stream.Save();
                    CloseOKs(true);
                }

                #region OpenedKeyManager

                Dictionary<string, OrderedKeys<long>> openOKs = new Dictionary<string, OrderedKeys<long>>();
                public OrderedKeys<long> GetOK(string name)
                {
                    OrderedKeys<long> res;
                    if(!openOKs.TryGetValue(name, out res))
                    {
                        res = new OrderedKeys<long>(table.db, name);
                        openOKs[res.name] = res;
                    }

                    return res;
                }

                public void CloseOKs(bool all = false)
                {
                    if (all)
                    {
                        foreach(var ok in openOKs)
                            ok.Value.stream.Save();         

                        openOKs.Clear();
                    }
                    else
                    {
                        const int maxOpen = 16;

                        if (openOKs.Count <= maxOpen) return;

                        var ordered = new OrderedDictionary<long, OrderedKeys<long>>();
                        foreach(var ok in openOKs)
                            ordered.Add(ok.Value.stream.lastChange, ok.Value);

                        int i = 0;
                        while(openOKs.Count > maxOpen && i < ordered.Items.Count())
                        {
                            var ok = ordered[i];
                            ok.stream.Save();
                            openOKs.Remove(ok.name);
                        }
                    }
                }

                #endregion

                public void Set (T row, long pos)
                {
                    var keysStack = new List<OrderedKeys<long>>() { Keys };

                    long nextKeys = 0;

                    for(int r=0; r < Relation.Length; r++) {
                        var path = string.Join("-", Relation.Take(r+1).ToArray());

                        if(keysStack.Count <= r)
                        {
                            var nk = GetOK(prefix+"-"+path+"-"+nextKeys.ToString("X"));
                            keysStack.Add(nk);
                        }

                        var keys = keysStack[r];

                        var rel = Relation[r];
                        var val = (long)table.type.members[rel].Extract(row);

                        if (r < Relation.Length - 1)
                        {
                            if (keys.Has(val))
                            {
                                nextKeys = keys.Get(val);
                            }
                            else
                            {
                                nextKeys = keys.keys.Last().Value + 1;
                                keys.Set(val, nextKeys);
                            }
                        }
                        else
                        {
                            keys.Set(val, pos);
                        }
                    }
                }

                public long Get(T row)
                {
                    var keysStack = new List<OrderedKeys<long>>() { Keys };

                    long nextKeys = 0;

                    for (int r = 0; r < Relation.Length; r++)
                    {
                        var path = string.Join("-", Relation.Take(r + 1).ToArray());

                        if (keysStack.Count <= r)
                        {
                            var nk = new OrderedKeys<long>(table.db, prefix + "-" + path + "-" + nextKeys.ToString("X"));
                            keysStack.Add(nk);
                        }

                        var keys = keysStack[r];

                        var rel = Relation[r];
                        var val = (long)table.type.members[rel].Extract(row);

                        if (keys.Has(val))
                        {
                            nextKeys = keys.Get(val);
                        }
                        else
                        {
                            return -1;
                        }
                    }

                    return nextKeys;
                }

                public void Delete (T row)
                {
                    var keysStack = new List<OrderedKeys<long>>() { Keys };
                    var keysAssoc = new List<long>();

                    long nextKeys = 0;

                    for (int r = 0; r < Relation.Length; r++)
                    {
                        var path = string.Join("-", Relation.Take(r + 1).ToArray());

                        if (keysStack.Count <= r)
                        {
                            var nk = GetOK(prefix + "-" + path + "-" + nextKeys.ToString("X"));
                            keysStack.Add(nk);
                        }

                        var keys = keysStack[r];

                        var rel = Relation[r];
                        var val = (long)table.type.members[rel].Extract(row);

                        if (keys.Has(val))
                        {
                            nextKeys = keys.Get(val);
                            keysAssoc.Add(nextKeys);
                        }
                        else
                            throw new Exception("Unexcepted missing of key");

                    }

                    bool prevDied = true;
                    for(int r = Relation.Length - 1; r >= 0; r--)
                    {
                        var keys = keysStack[r];
                        if (prevDied)
                        {
                            keys.Delete(keysAssoc[r]);
                        }

                        if (keys.keys.Items.Count() == 0 && r > 0)
                        {
                            keys.Die();
                            openOKs.Remove(keys.name);
                            
                            prevDied = true;
                        }
                        else
                            prevDied = false;
                    }
                }
            }

            #endregion

            public void Insert (T row)
            {
                var bytes = type.ObjToBytes(row);
                var index = bytesTable.Set(bytes);

                foreach(var key in Keys)
                    key.Set(row, index);
            }

            public long GetIndex (T row, string relation=null)
            {
                if (relation == null) relation = Keys[0].name;
                relation = relation.Replace(" ", "");

                long index = -1;
                foreach(var key in Keys)
                {
                    if(key.name == relation)
                    {
                        index = key.Get(row);
                        break;
                    } 
                }

                return index;
            }

            public T Get(long index)
            {
                var bytes = bytesTable.Get(index);
                var res = (T)type.BytesToObject(bytes);
                return res;
            }

            public T Get(T row, string relation = null)
            {
                var index = GetIndex(row, relation);
                return Get(index);
            }

            public void Update(T row, string relation = null)
            {
                var index = GetIndex(row, relation);
                var bytes = type.ObjToBytes(row);
                bytesTable.Set(bytes, index);

                // Update keys
                //todo: check if key are changed
                foreach (var key in Keys)
                    key.Set(row, index);
            }

            public void Delete(long index)
            {
                var row = Get(index);
                Delete(row, null, index);
            }

            public void Delete(T row, string relation = null, long index=-1)
            {
                if(index == -1)
                    index = GetIndex(row, relation);

                Delete(index);

                foreach (var key in Keys)
                    key.Delete(row);
            }
        }

        #region InternalStructs

        struct DataIndex
        {
            public long Index;
            public int Size;
        }

        #endregion

        #region Tables

        public struct FS
        {
            public long ID;
            public long Parent;

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
