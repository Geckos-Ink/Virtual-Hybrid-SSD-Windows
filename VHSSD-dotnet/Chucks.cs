using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Chucks
    {
        VHFS vhfs;

        DB.Table<DB.Chuck> tableChuck;

        Dictionary<long, Dictionary<long, Chuck>> chucks = new Dictionary<long, Dictionary<long, Chuck>>();

        Timer timerDispose;

        public Chucks(VHFS vhfs)
        {
            this.vhfs = vhfs;

            tableChuck = vhfs.DB.GetTable<DB.Chuck>();
            tableChuck.SetKey("ID", "Part");

            timerDispose = new Timer(TimerDispose, null, 0, 1000);
        }

        Random rand = new Random();
        OrderedDictionary<long, Chuck> chucksTemperature = new OrderedDictionary<long, Chuck>();
        public void TimerDispose(object state)
        {
            chucksTemperature.Clear();

            foreach(var idChucks in chucks)
            {
                foreach(var chuck in idChucks.Value)
                {
                    var temp = chuck.Value.chuckRow.Temperature;

                    // Differentiate randomly temperature
                    int var = 1;
                    while (chucksTemperature.Has(temp))
                    {
                        temp += rand.Next(var * -1, var);
                        var++;
                    }

                    chucksTemperature.Add(temp, chuck.Value);
                }
            }
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

                part.part = pos / vhfs.chuckSize;
                part.pos = pos % vhfs.chuckSize;
                part.length = vhfs.chuckSize - part.pos;

                parts.Add(part);

                pos += part.length;
            }

            part.length = (pos + length) % vhfs.chuckSize;

            return parts.ToArray();
        }

        public Chuck GetChuck(long id, long part)
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
                chuck = new Chuck(this, id, part);
                idChucks[part] = chuck;
            }

            return chuck;
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

            public DB.Chuck chuckRow;

            File fileHDD;
            File fileSSD;

            bool onHDD = false;

            byte[] data;

            public long LastUsage;
            public long LastWrite;
            public long LastRead;

            public double AvgUsage;
            public double Usages = 0;

            public Chuck(Chucks chucks, long id, long part)
            {
                this.chucks = chucks;

                chuckRow = new DB.Chuck();
                chuckRow.ID = id;
                chuckRow.Part = part;

                LoadRow();

                LastUsage = Static.UnixTimeMS;
            }

            public void LoadRow()
            {
                var row = chucks.tableChuck.Get(chuckRow, "ID,Part");
                if (row == null)
                {
                    chuckRow.SSD_ID = chucks.vhfs.GetRandomDrive(true).id;
                    chuckRow.HDD_ID = chucks.vhfs.GetRandomDrive(false).id;
                }
                else
                {
                    chuckRow = row;
                }
            }

            public void LoadFile(bool all = false)
            {
                var chuckName = chuckRow.ID.ToString("X")+"_"+chuckRow.Part.ToString("X")+".bin";

                if(fileSSD == null && chuckRow.SSD_ID >= 0)
                {
                    var drive = chucks.vhfs.SSDDrives[chuckRow.SSD_ID];
                    fileSSD = new File(drive.Dir + chuckName, drive);
                }

                if(chuckRow.SSD_ID < 0 || all || !SSDUpdated())
                {
                    var drive = chucks.vhfs.HDDDrives[chuckRow.HDD_ID];
                    fileHDD = new File(drive.Dir + chuckName, drive);
                }
            }

            public bool SSDUpdated()
            {
                return chuckRow.SSD_Version >= chuckRow.HDD_Version;
            }

            public bool SSDGreater()
            {
                return chuckRow.SSD_Version > chuckRow.HDD_Version;
            }

            public bool HDDGreater()
            {
                return chuckRow.SSD_Version < chuckRow.HDD_Version;
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
                    chuckRow.HDD_Version += 1;

                if (!onHDD && !SSDGreater())
                    chuckRow.SSD_Version += 1;
            }

            public long CalculateAvgUsage()
            {
                // More precision to temperature to compensante integer flooring
                double temp = ((chuckRow.Temperature * Usages) + (chucks.Traffic*100)) / (Usages + 1);
                chuckRow.Temperature = (long)temp;

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
                var res = file.Read(part.length, part.pos);

                LastRead = CalculateAvgUsage();

                InOperation = false;
                return res;
            }

            public void Write(Part part, byte[] bytes)
            {
                InOperation = true;

                var file = BestDrive();
                file.Write(bytes, part.pos);

                LastWrite = CalculateAvgUsage();
                
                IncreaseVersion();

                InOperation = false;
            }

            public void Resize(Part part)
            {
                LoadFile(true);

                if(part.length < chucks.vhfs.chuckSize)
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
        }
    }
}
