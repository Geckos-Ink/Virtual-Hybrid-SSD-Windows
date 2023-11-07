using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Chucks
    {
        VHFS vhfs;

        public DB.Table<DB.Chuck> tableChuck;

        public Dictionary<long, Dictionary<long, Chuck>> chucks = new Dictionary<long, Dictionary<long, Chuck>>();

        public Chucks(VHFS vhfs)
        {
            this.vhfs = vhfs;

            tableChuck = vhfs.DB.GetTable<DB.Chuck>();
            tableChuck.SetKey("ID", "Part");
            tableChuck.SetKey("Temperature");
            tableChuck.SetKey("LastUsage");
        } 

        public struct Part
        {
            public long part;
            public long resPos;
            public long pos;
            public long length;
        }

        public Part[] GetParts(long pos, long length)
        {
            List<Part> parts = new List<Part>();

            long start = pos;
            long reach = pos + length;

            Part part;
            while (pos <= reach)
            {
                part = new Part();

                part.resPos = pos - start;

                part.part = pos / vhfs.Sets.chuckSize;
                part.pos = pos % vhfs.Sets.chuckSize;
                part.length = vhfs.Sets.chuckSize - part.pos;

                parts.Add(part);

                pos += part.length;
            }

            part.length = (pos + length) % vhfs.Sets.chuckSize;

            return parts.ToArray();
        }

        public Chuck GetChuck(long id, long part, DB.Chuck row = null)
        {
            Dictionary<long, Chuck> idChucks;

            if(!chucks.TryGetValue(id, out idChucks))
            {
                idChucks = new Dictionary<long, Chuck>();
                chucks[id] = idChucks;
            }

            Chuck chuck;
            if(!idChucks.TryGetValue(part, out chuck))
            {
                chuck = new Chuck(this, id, part, row);
                idChucks[part] = chuck;
            }

            return chuck;
        }

        public Chuck GetChuck(DB.Chuck row)
        {
            return GetChuck(row.ID, row.Part, row);
        }

        public void RemoveChuck(long id, long part)
        {
            Dictionary<long, Chuck> idChucks;
            if (chucks.TryGetValue(id, out idChucks))
            {
                Chuck chuck;
                if (idChucks.TryGetValue(part, out chuck))
                {
                    idChucks.Remove(part);

                    if (idChucks.Count == 0)
                        chucks.Remove(id);
                }
            }
        }

        #region Stats

        public long AvgBytesRead = 0;
        Dictionary<long, long> bytesRead = new Dictionary<long, long>();
        void addReadBytes(long length)
        {
            var now = Static.UnixTime;
            if (bytesRead.ContainsKey(now))
            {
                bytesRead[now] += length;
            }
            else
            {
                if (bytesRead.Count > 0)
                {
                    var prev = bytesRead.First();
                    AvgBytesRead = (AvgBytesRead + prev.Value) / 2;
                    bytesRead.Remove(prev.Key);
                }


                bytesRead.Add(now, length);
            }
        }

        public long AvgBytesWrite = 0;
        Dictionary<long, long> bytesWrite = new Dictionary<long, long>();
        void addWriteBytes(long length)
        {
            var now = Static.UnixTime;
            if (bytesWrite.ContainsKey(now))
            {
                bytesWrite[now] += length;
            }
            else
            {
                if (bytesWrite.Count > 0)
                {
                    var prev = bytesWrite.First();
                    AvgBytesWrite = (AvgBytesWrite + prev.Value) / 2;
                    bytesWrite.Remove(prev.Key);
                }

                bytesWrite.Add(now, length);
            }
        }

        public long Traffic
        {
            get { return AvgBytesRead + AvgBytesRead; }
        }

        #endregion

        public byte[] Read(long ID, long pos, long length)
        {
            var bytes = new byte[length];

            var parts = GetParts(pos, length);
            foreach (var part in parts)
            {
                var chuck = GetChuck(ID, part.part);
                chuck.inUsing = true;
                var res = chuck.Read(part);
                res.CopyTo(bytes, part.resPos);

                addReadBytes(res.Length);
            }

            return bytes;
        }

        public void Write(long ID, long pos, byte[] bytes)
        {
            var parts = GetParts(pos, bytes.LongLength);
            foreach (var part in parts)
            {
                var bb = new byte[part.length];
                Array.Copy(bytes, part.resPos, bb, part.pos, part.length);
                var chuck = GetChuck(ID, part.part);
                chuck.inUsing = true;
                chuck.Write(part, bb);
            }
        }

        public void Resize(long ID, long from, long to)
        {
            long diffLength = from - to;
            var parts = GetParts(to, diffLength);

            foreach(var part in parts)
            {
                var chuck = GetChuck(ID, part.part);
                chuck.Resize(part);
            }
        }

        public class Chuck
        {
            Chucks chucks;

            public DB.Chuck row;

            File fileHDD;
            File fileSSD;

            bool onHDD = false;

            RamFile ramFile = new RamFile();

            // Operations
            public bool onExchange = false;
            public bool inUsing = false;
            public bool inWrite = false; // during this instance was performed at least a write operation

            public Chuck(Chucks chucks, long id, long part, DB.Chuck row = null)
            {
                this.chucks = chucks;

                row = new DB.Chuck();
                row.ID = id;
                row.Part = part;

                if (row != null)
                    this.row = row;
                else
                    LoadRow();

                LastUsage = Static.UnixTimeMS;
            }

            #region Properties 

            public long LastUsage
            {
                get { return row.LastUsage; }
                set { row.LastUsage = value; }
            }

            public double Usages
            {
                get { return row.Usages; }
                set { row.Usages = value; }
            }

            public double AvgUsage
            {
                get { return row.AvgUsage; }
                set { row.AvgUsage = value; }
            }

            #endregion

            #region LoadSave

            public void LoadRow()
            {
                var _row = chucks.tableChuck.Get(row, "ID,Part");
                if (_row == null)
                {
                    row.SSD_ID = chucks.vhfs.GetBestDrive(true).id;
                    row.HDD_ID = chucks.vhfs.GetBestDrive(false).id;
                }
                else
                {
                    row = _row;
                }
            }

            public void SaveRow()
            {
                chucks.tableChuck.Set(row);
            }

            public void LoadFile(bool all = false)
            {
                var chuckName = row.ID.ToString("X")+"_"+row.Part.ToString("X")+".bin";

                if(fileSSD == null && row.SSD_ID >= 0)
                {
                    var drive = chucks.vhfs.AllDrives[row.SSD_ID];
                    fileSSD = new File(drive.Dir + chuckName, drive);
                    onHDD = false;
                }

                // in this moment the chuck is always present on HDD
                if(row.SSD_ID < 0 || all || !SSDUpdated())
                {
                    var drive = chucks.vhfs.AllDrives[row.HDD_ID];
                    fileHDD = new File(drive.Dir + chuckName, drive);
                    onHDD = true;
                }
            }

            public void Close()
            {
                if (fileSSD != null)
                    fileSSD.Close();

                if (fileHDD != null)
                    fileHDD.Close();



                chucks.RemoveChuck(row.ID, row.Part);
            }

            #endregion

            public bool SSDUpdated()
            {
                return row.SSD_Version >= row.HDD_Version;
            }

            public bool SSDGreater()
            {
                return row.SSD_Version > row.HDD_Version;
            }

            public bool HDDGreater()
            {
                return row.SSD_Version < row.HDD_Version;
            }

            public File BestDrive()
            {
                if (fileSSD != null && SSDUpdated())
                {
                    onHDD = false;
                    return fileSSD;
                }
                else
                {
                    onHDD = true;
                    return fileHDD;
                }
            }

            public void IncreaseVersion()
            {
                if (onHDD && !HDDGreater())
                    row.HDD_Version += 1;

                if (!onHDD && !SSDGreater())
                    row.SSD_Version += 1;
            }

            public long CalculateAvgUsage()
            {
                // More precision to temperature to compensante integer flooring
                double temp = ((row.Temperature * Usages) + (chucks.Traffic*100)) / (Usages + 1);
                row.Temperature = (long)temp;

                long now = Static.UnixTimeMS;

                double diff = now - LastUsage;
                AvgUsage = (diff + AvgUsage * Usages) / (Usages+1);
                Usages++;

                LastUsage = now;
                return now;
            }


            public bool InOperation = false;

            public byte[] Read(Part part)
            {
                InOperation = true;

                var file = BestDrive();

                if (onHDD)
                {
                    // Make a decision: copy it on SSD or read directly as-is?
                }

                var res = file.Read(part.length, part.pos);

                row.LastRead = CalculateAvgUsage();

                InOperation = false;
                return res;
            }

            public void Write(Part part, byte[] bytes)
            {
                InOperation = true;
                inWrite = true;

                var file = BestDrive();

                if (onHDD)
                    ramFile.Write(bytes, part.pos);
                else 
                    file.Write(bytes, part.pos);

                row.LastWrite = CalculateAvgUsage();
                
                IncreaseVersion();

                InOperation = false;
            }

            public void Resize(Part part)
            {
                LoadFile(true);

                if(part.length < chucks.vhfs.Sets.chuckSize)
                {
                    if (fileSSD != null)
                        fileSSD.Length = part.length;

                    if (fileHDD != null)
                        fileHDD.Length = part.length;
                }
                else
                {
                    if (fileSSD != null)
                        fileSSD.Delete();

                    if(fileHDD != null)
                        fileHDD.Delete();
                }
            }

            #region Sync

            public void SyncVersion()
            {
                bool moveToHdd = row.SSD_Version > row.HDD_Version;

                File from = fileHDD;
                File to = fileSSD; 

                if (moveToHdd)
                {
                    from = fileSSD;
                    to = fileSSD;
                }

                from.CopyTo(to);

                row.SSD_Version = row.HDD_Version = 0; 
            }

            public void MoveToHDD()
            {
                SyncVersion();

                fileSSD.Delete();

                row.SSD_Version = -1;
                row.OnSSD = false;
            }

            #endregion
        }
    }
}
