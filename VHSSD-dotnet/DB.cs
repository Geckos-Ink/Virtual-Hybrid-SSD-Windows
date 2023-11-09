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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VHSSD
{
    public class DB
    {
        VHFS vhfs;
        string dir;

        public DB(VHFS vhfs) 
        {
            this.vhfs = vhfs;

            var dataDir = "db/";
            Static.CreateDirIfNotExists(dataDir);

            dir = dataDir + vhfs.Name+"/";
            Static.CreateDirIfNotExists(dir);
        }

        #region BytesTables 

        public Dictionary<int, BytesTable> bytesTables = new Dictionary<int, BytesTable>();
        BytesTable GetBytesTable(int size)
        {
            if(!bytesTables.ContainsKey(size))
                bytesTables.Add(size, new BytesTable(this, size));

            return bytesTables[size];
        }

        public class BytesTable
        {
            DB db;
            long size;

            List<long> freeSlots = new List<long>();
            ListStream<long> freeSlotsStream;

            public File fileValues;

            public BytesTable(DB db, int size)
            {
                this.db = db;
                this.size = (long)size;

                if (size == 0) return;

                this.freeSlotsStream = new ListStream<long>(db, "bt-fs-" + size, freeSlots);

                fileValues = new File(db.dir + "bt-" + size + ".bin");
            }

            public byte[] Get(long index)
            {
                if (size == 0) return new byte[0];

                return fileValues.Read(size, size * index);
            }

            public long Set(byte[] value, long index = -1)
            {
                if (size == 0) return 0;

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
                if (size == 0) return;

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
        public Type GetType(System.Type type)
        {
            if (!types.ContainsKey(type))
                types.Add(type, new Type(this, type));

            return types[type];
        }

        public class Type
        {
            DB db;

            public System.Type type;
            public System.Type originalType;
            public System.Type elementType;

            public Member firstMember;
            public OrderedDictionary<string, Member> members;

            public string name;
            public int size = -1;

            public bool isString = false;
            public bool isValue = false;

            public bool hasDynamicSize = false;

            public System.Type[] iterateTypes;
            public bool isList = false;
            public bool isDictionary = false;

            // Precached methods
            MethodInfo toArrayMethod;
            MethodInfo toStringMethod;

            public Type(DB db, System.Type type)
            {
                this.db = db;

                originalType = type;

                if (type == typeof(string))
                {
                    type = typeof(char[]);
                    toStringMethod = type.GetMethod("ToString");
                    isString = true;
                }

                if (type.IsGenericType)
                {
                    // Get generic type argument(s)
                    iterateTypes = type.GetGenericArguments();

                    var genType = type.GetGenericTypeDefinition();

                    isList = genType == typeof(List<>);
                    isDictionary = genType == typeof(Dictionary<,>);

                    if (isList)
                    {
                        var t = iterateTypes[0];
                        type = t.MakeArrayType();

                        toArrayMethod = originalType.GetMethod("ToArray");
                    }

                    if (isDictionary)
                    {
                        throw new Exception("Dictionaries needs still to be implemented");
                    }

                    //if (!isList && !isDictionary)
                    //    Console.WriteLine("Possible unsupported generic type");
                }

                ///
                ///

                this.type = type;

                name = type.Name;
                if (type.IsArray)
                {
                    size = db.GetType(typeof(DataIndex)).size; //todo: cache DataIndex size
                    hasDynamicSize = true;

                    elementType = type.GetElementType();
                }
                else if (type.IsClass || type.IsConstructedGenericType)
                {
                    this.members = new OrderedDictionary<string, Member>();

                    size = 0;

                    var members = type.GetFields();
                    foreach (var member in members)
                    {
                        var m = new Member(db, member);

                        if (firstMember == null) firstMember = m;

                        this.members.Add(member.Name, m);

                        if (m.type.hasDynamicSize)
                            hasDynamicSize = true;

                        size += m.size;
                    }
                }
                else if (type.IsValueType)
                {
                    isValue = true;

                    // Better to obtain the size empirically
                    var def = Activator.CreateInstance(type);
                    var bytes = ObjToBytes(def);

                    size = bytes.Length; // Marshal.SizeOf(type);
                }
                else
                    throw new Exception("What is das?");
            }

            public bool CompareObjs(object obj1, object obj2)
            {
                if (type.IsValueType || isString)
                {
                    return obj1 == obj2;
                }

                if (type.IsArray)
                {
                    var a1 = ((Array)obj1).Cast<object>();
                    var a2 = ((Array)obj2).Cast<object>();
                    return a1.SequenceEqual(a2);
                }

                foreach(var member in this.members.Items)
                {
                    var m1 = member.Value.Extract(obj1);
                    var m2 = member.Value.Extract(obj2);

                    if (m1 == m2 && m1 == null) return true;
                    if (m1 == null || m2 == null) return false;

                    var t = db.GetType(m1.GetType());
                    if (t.CompareObjs(m1, m2))
                        return true;
                }

                return true;
            }

            #region ReflectedCast

            private static List<T> ConvertList<T>(List<object> inputList)
            {
                return inputList.OfType<T>().ToList();
            }

            MethodInfo toListMethod;

            object toList(List<object> list) // doesn't work
            {
                var baseType = iterateTypes[0];

                if (toListMethod == null)
                {
                    toListMethod = typeof(Type)
                    .GetMethod(nameof(ConvertList), BindingFlags.Static | BindingFlags.NonPublic)
                    .MakeGenericMethod(baseType);
                }

                // Call the generic method to convert List<object> to List<long>
                return toListMethod.Invoke(null, new object[] { list });
            }

            #endregion

            public byte[] ObjToBytes (object obj)
            {
                if (isString)
                    obj = ((string)obj).ToCharArray();

                if (isValue)
                {
                    if(type == typeof(byte))
                        return BitConverter.GetBytes((byte)obj);

                    if (type == typeof(char))
                        return BitConverter.GetBytes((char)obj);

                    if (type == typeof(bool))
                        return BitConverter.GetBytes((bool)obj);

                    if (type == typeof(short))
                        return BitConverter.GetBytes((short)obj);

                    if (type == typeof(ushort))
                        return BitConverter.GetBytes((ushort)obj);

                    if (type == typeof(int))
                        return BitConverter.GetBytes((int)obj);

                    if (type == typeof(uint))
                        return BitConverter.GetBytes((uint)obj);

                    if (type == typeof(long))
                        return BitConverter.GetBytes((long)obj);

                    if (type == typeof(ulong))
                        return BitConverter.GetBytes((ulong)obj);

                    if (type == typeof(float))
                        return BitConverter.GetBytes((float)obj);

                    if (type == typeof(double))
                        return BitConverter.GetBytes((double)obj);
                }
                else
                {
                    if (type.IsArray)
                    {
                        if (isList)
                        {
                            // Convert to array
                            object[] parameters = new object[] { };
                            obj = toArrayMethod.Invoke(obj, parameters);
                        }

                        List<byte> resBytes = new List<byte>();

                        var arrayOf = db.GetType(type.GetElementType());

                        // Better a void array than a null object
                        if (obj == null)
                            obj = new object[0];

                        var arr = ((Array)obj).Cast<object>().ToArray();
                        foreach (var item in arr)
                        {
                            var bytes = arrayOf.ObjToBytes(item);
                            resBytes.AddRange(bytes);
                        }

                        var index = new DataIndex();
                        index.Size = resBytes.Count;
                        index.Index = db.GetBytesTable(index.Size).Set(resBytes.ToArray());

                        var tDataIndex = db.GetType(typeof(DataIndex));
                        var res = tDataIndex.ObjToBytes(index);
                        return res;
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
                    if(bytes == null || bytes.Length == 0) 
                        return Activator.CreateInstance(type); // return default value

                    if(type == typeof(char)) 
                        return Encoding.UTF8.GetString(bytes)[0];

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
                        for(int i = 0; i< allBytes.Length; i+= arrayOf.size)
                        {
                            var itemData = allBytes.Skip(i).Take(arrayOf.size).ToArray();
                            var obj = arrayOf.BytesToObject(itemData);
                            list.Add(obj);
                        }

                        if (isString)
                            return new String(list.Select(o => (char)o).ToArray());

                        if (isList)
                            return toList(list);

                        var arr = list.ToArray();

                        if(isString)
                            return toStringMethod.Invoke(arr, new object[] { });

                        return arr;
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

                    var type = info.FieldType;
                    this.type = db.GetType(type);

                    if (type.IsArray)
                        size = db.GetType(typeof(DataIndex)).size;
                    else
                        size = this.type.size;
                }

                public object Extract(object obj)
                {
                    if(obj == null) return null;

                    return info.GetValue(obj);
                }

                public void Set(object obj, object val)
                {
                    // Force array casting when necessary
                    if (!type.isString && type.type.IsArray && !type.isList && val.GetType().GetElementType() != type.elementType)
                    {
                        var arr = (object[])val;
                        Array convertedArray = Array.CreateInstance(type.elementType, arr.Length);
                        for (int i = 0; i < arr.Length; i++)
                        {
                            // Perform the conversion using Convert.ChangeType
                            object convertedValue = Convert.ChangeType(arr[i], type.elementType);
                            convertedArray.SetValue(convertedValue, i);
                        }

                        val = convertedArray;
                    }

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

            public OrderedDictionary<T, List<long>> keys = new OrderedDictionary<T, List<long>>(); 
            public OrderedDictionaryStream<T, List<long>> stream;

            public OrderedKeys(DB db, string name)
            {
                this.db = db;
                this.name = name;

                stream = new OrderedDictionaryStream<T, List<long>>(db, name, keys);
            }

            public void Set(T key, long id)
            {
                if (keys.Has(key))
                {
                    var kk = keys[key];
                    if (kk.IndexOf(id) == -1)
                    {
                        kk.Add(id);
                        keys[key] = kk;
                    }
                }
                else
                    keys.Add(key, new List<long> { id });

                stream.Changed = true;
            }

            public long Get(T key)
            {
                return GetAll(key).FirstOrDefault();
            }

            public List<long> GetAll(T key)
            {
                stream.Changed = stream.Changed; // warn of the usage
                return keys[key];
            }

            public bool Delete(T key, long index)
            {
                var kk = GetAll(key);

                bool removeAll = false;
                if (index >= 0)
                {
                    if (!kk.Contains(index))
                        return false;

                    if (kk.Count == 1)
                    {  
                        removeAll = true;
                    }
                    else
                    {
                        kk.Remove(index);
                        keys[key] = kk;
                    }
                }
                else
                    removeAll = true;

                if (removeAll)
                    keys.Remove(key);

                stream.Changed = true;

                return true;
            }

            public bool Has(T key)
            {
                return keys.Has(key);
            }

            public void Die()
            {
                stream.file.Delete();
            }

            #region Math 

            public T Min()
            {
                return keys.Items.First().Key;
            }

            public T Max()
            {
                return keys.Items.Last().Key;
            }

            // Put double instead of T?
            public T Avg()
            {
                if (keys.Items.Count == 0)
                    return default(T);

                double res = 0;

                foreach (var item in keys.Items)
                    res += Convert.ToDouble(item.Key);

                res /= keys.Items.Count;

                return (T)Convert.ChangeType(res, typeof(T));
            }

            #endregion
        }

        #endregion

        #region IterableStreams

        public List<IterateStream> iterateStreams = new List<IterateStream>();
        
        public abstract class IterateStream
        {
            internal DB db;
            internal File file;

            public IterateStream(DB db)
            {
                db.iterateStreams.Add(this);
            }

            public long lastChange = 0;
            bool changed = false;
            public bool Changed
            {
                get { return changed; }

                set {
                    lastChange = Static.UnixTimeMS;
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

            public ListStream(DB db, string name, List<T> list) : base(db)
            {
                this.db = db;
                this.list = list;

                getSetType = db.GetType(typeof(T));

                this.InitSaveChecker();

                file = new File(db.dir + "list-" + name+".bin");

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

            class KeyValue<TT, VV>
            {
                public TT Key;
                public VV Value;
            }

            public OrderedDictionaryStream(DB db, string name, OrderedDictionary<T, V> dict) : base(db)
            {
                this.db = db;
                this.dict = dict;

                keyValueType = db.GetType(typeof(KeyValue<T,V>));

                this.InitSaveChecker();

                file = new File(db.dir + "odict-" + name+".bin");

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
                    var obj = keyValueType.BytesToObject(rowData);
                    var kv = (KeyValue<T, V>)obj;

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
        public Table<T> GetTable<T>(string ctx = "") where T : DBRow
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

        public class Table<T> where T : DBRow
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

                type = db.GetType(typeof(T));

                this.ctx = type.name + (String.IsNullOrEmpty(ctx) ? "" : "-" + ctx);

                RowSize = type.size;
                bytesTable = db.GetBytesTable(RowSize);
            }

            public void CheckKey()
            {
                if (Keys.Count == 0)
                    SetKey(type.firstMember.info.Name);
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

                var name = String.Join(",", keys);

                foreach (var key in Keys)
                    if (key.name == name)
                        return; //it already exists

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

                /// <summary>
                /// This method returns the unique OrderedKeys possible
                /// </summary>
                /// <returns></returns>
                /// <exception cref="Exception">If more Keys are possible</exception>
                public OrderedKeys<long> GetOrderedKeys()
                {
                    if (Relation.Length != 1)
                        throw new Exception("GetOrderedKeys compatible only with unique key");

                    return GetOK(Relation[0]);
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
                            var ok = ordered[i++];
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
                        var relMemb = table.type.members[rel];
                        var vobj = relMemb.Extract(row);

                        long val = 0;

                        // Manual converting when necessary
                        if (relMemb.type.type == typeof(short))
                            val = (short)vobj;
                        else
                            val = (long)vobj;

                        if (r < Relation.Length - 1)
                        {
                            if (keys.Has(val))
                            {
                                nextKeys = keys.Get(val);
                            }
                            else
                            {
                                nextKeys = 0;
                                if(keys.keys.Items.Count > 0)
                                    nextKeys = keys.keys.Last().Value.Max() + 1;

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
                    var allKeys = GetAll(row);

                    if(row.AbsIndex >= 0)
                    {
                        var i = allKeys.ToList().IndexOf(row.AbsIndex);
                        if (i == -1) return i;
                        return allKeys[i];
                    }

                    return allKeys.FirstOrDefault();
                }

                public List<long> GetAll(T row)
                {
                    var keysStack = new List<OrderedKeys<long>>() { Keys };

                    List<long> nextKeys = null;

                    for (int r = 0; r < Relation.Length; r++)
                    {
                        var path = string.Join("-", Relation.Take(r + 1).ToArray());

                        if (keysStack.Count <= r)
                        {
                            if (nextKeys == null || nextKeys.Count == 0)
                                return null;

                            var nk = GetOK(prefix + "-" + path + "-" + nextKeys.First().ToString("X"));
                            keysStack.Add(nk);
                        }

                        var keys = keysStack[r];

                        var rel = Relation[r];
                        var val = Convert.ToInt64(table.type.members[rel].Extract(row));

                        if (keys.Has(val))
                        {
                            nextKeys = keys.GetAll(val);
                        }
                        else
                        {
                            return null;
                        }
                    }

                    return nextKeys;
                }

                public bool Delete (T row) 
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
                            keysAssoc.Add(val);
                        }
                        else
                            return false;

                    }

                    bool prevDied = true;
                    for(int r = Relation.Length - 1; r >= 0; r--)
                    {
                        var keys = keysStack[r];
                        if (prevDied)
                        {
                            long index = -1;
                            if (r == Relation.Length - 1)
                                index = row.AbsIndex;

                            keys.Delete(keysAssoc[r], index);
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

                    return true;
                }
            }

            public Key GetKey(string relation)
            {
                relation = relation?.Replace(" ", "");

                foreach (var key in Keys)
                {
                    if (relation == null || key.name == relation)
                    {
                        return key;
                    }
                }

                return null;
            }

            struct WhereMethod
            {
                public object value;
                public Type.Member member;
            }

            public OrderedDictionary<long, List<long>> AvgKeys(string key1, string key2, T where = null)
            {
                if (key1.Contains(",") || key2.Contains(","))
                    throw new Exception("Only single keys are supported");

                var checkWhere = new List<WhereMethod>();
                if(where != null)
                {
                    foreach(var member in type.members.Items)
                    {
                        var wv = member.Value.Extract(where);
                        if (wv != null)
                        {
                            var wm = new WhereMethod() { value = wv, member = member.Value };
                            checkWhere.Add(wm);
                        }
                    }
                }

                OrderedDictionary<long, List<long>> orderedKeys = new OrderedDictionary<long, List<long>>();

                var ks1 = GetKey(key1);
                var ks2 = GetKey(key2);

                var kks1 = ks1.Keys.keys;
                var kks2 = ks2.Keys.keys;

                if (kks1.Items.Count == 0 || kks2.Items.Count == 0)
                    return orderedKeys;

                var min1 = kks1.Items.First().Key;
                var min2 = kks2.Items.First().Key;
                var max1 = kks1.Items.Last().Key;
                var max2 = kks2.Items.Last().Key; 

                var nkeys = kks1.Items.Count();
                for(int i= 0; i < nkeys; i++)
                {
                    var values = kks1.Items[i].Value;

                    foreach (var val in values)
                    {
                        var row = Get(val);

                        foreach(var w in checkWhere)
                        {
                            var wv = w.member.Extract(row);
                            if (wv != w.value)
                                continue;
                        }

                        var p1 = (long)type.members[key1].Extract(row);
                        var p2 = (long)type.members[key2].Extract(row);

                        var k1 = (double)(p1 - min1) / (max1 - min1) * long.MaxValue;
                        var k2 = (double)(p2 - min2) / (max2 - min2) * long.MaxValue;

                        var avg = (long)((k1 + k2) / 2);

                        if (orderedKeys.Has(avg))
                            orderedKeys[avg].Add(val);
                        else
                            orderedKeys.Add(avg, new List<long> { val });
                    }
                }

                return orderedKeys;
            }

            #endregion

            public long Set (T row, string relation=null, long index = -1)
            {
                CheckKey();

                if (row.AbsIndex >= 0) index = row.AbsIndex;
                if (index == -1) index = GetIndex(row, relation);

                var bytes = type.ObjToBytes(row);
                index = bytesTable.Set(bytes, index);

                row.AbsIndex = index;

                foreach(var key in Keys)
                    key.Set(row, index);

                return index;
            }

            public List<long> GetAllIndex(T row, string relation = null)
            {
                CheckKey();

                List<long> indexes = null;
                var key = GetKey(relation);

                if(key != null)
                    indexes = key.GetAll(row);

                return indexes;
            }

            public long GetIndex (T row, string relation=null)
            {
                var indexes = GetAllIndex(row, relation);

                if(indexes == null || indexes.Count == 0) return -1;

                if(row.AbsIndex >= 0)
                {
                    var i = indexes.ToList().IndexOf(row.AbsIndex);
                    if (i == -1) return i;
                    return indexes.Last();
                }

                return indexes.First();
            }

            public T Get(long index)
            {
                var bytes = bytesTable.Get(index);
                var res = (T)type.BytesToObject(bytes);
                res.AbsIndex = index;
                return res;
            }

            public T Get(T row, string relation = null)
            {
                var index = GetIndex(row, relation);

                if (index == -1)
                    return default(T);

                return Get(index);
            }

            public T[] GetAll(T row, string relation = null)
            {
                var res = new List<T>();
                var indexes = GetAllIndex(row, relation);

                foreach(var i in indexes)
                {
                    res.Add(Get(i));
                }

                return res.ToArray();
            }

            public void Delete(long index)
            {
                var row = Get(index);
                Delete(row, null, index);
            }

            public void Delete(T row, string relation = null, long index=-1)
            {
                if (index == -1)
                    index = GetIndex(row, relation);

                bytesTable.Delete(index);

                foreach (var key in Keys)
                    key.Delete(row);
            }
        }

        #region InternalStructs

        class DataIndex
        {
            public long Index;
            public int Size;
        }

        #endregion

        #region Tables

        public abstract class DBRow
        {
            public long AbsIndex { get; set; }

            public DBRow()
            {
                AbsIndex = -1;
            }

            public T Clone<T>() where T : DBRow
            {
                string serializedObject = JsonConvert.SerializeObject(this);
                return JsonConvert.DeserializeObject<T>(serializedObject);
            }
        }

        public class FS : DBRow
        {
            public long ID;
            public long Parent;

            public string Name;

            public bool IsDirectory;
            public long[] Files;

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

        public class Chuck : DBRow
        {
            public long ID;
            public long Part;

            public bool OnSSD = true; // by default, is written on SSD

            public short SSD_ID = -1;
            public short SSD_Version = -1;

            public short HDD_ID = -1;
            public short HDD_Version = -1;

            public long Temperature = 0;

            public long LastUsage;
            public long LastWrite;
            public long LastRead;

            public double AvgUsage;
            public double Usages = 0;
        }

        public class Drive : DBRow
        {
            public short ID;
            public long UsedBytes;
        }

        #endregion
    }
}
