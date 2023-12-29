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

            // Stats
            TotalBytes = new Stats();
            BytesRead = new Stats(TotalBytes);
            BytesWrite = new Stats(TotalBytes);
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
            while (pos < reach)
            {
                part = new Part();

                part.resPos = pos - start;

                part.part = pos / vhfs.Sets.chuckSize;
                part.pos = pos % vhfs.Sets.chuckSize;

                part.length = (reach - pos);
                if (part.length > vhfs.Sets.chuckSize)
                    part.length = vhfs.Sets.chuckSize;

                parts.Add(part);

                pos += part.length;
                length -= part.length;
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
                chucks.Add(id, idChucks);
            }

            Chuck chuck;
            if(!idChucks.TryGetValue(part, out chuck))
            {
                chuck = new Chuck(this, id, part, row);
                idChucks.Add(part, chuck);
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

        public Stats TotalBytes;

        public Stats BytesRead;
        void addReadBytes(long length)
        {
            BytesRead.Add(length);
        }

        public Stats BytesWrite;
        void addWriteBytes(long length)
        {
            BytesWrite.Add(length);
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
                var bb = new byte[part.pos + part.length];
                Array.Copy(bytes, part.resPos, bb, part.pos, part.length);
                var chuck = GetChuck(ID, part.part);
                chuck.inUsing = true;
                chuck.Write(part, bb);

                addWriteBytes(bb.Length);
            }
        }

        public void Resize(long ID, long from, long to)
        {
            long diffLength = from - to;

            if (diffLength > 0)
            {
                var parts = GetParts(to, diffLength);

                foreach (var part in parts)
                {
                    var chuck = GetChuck(ID, part.part);

                    if (part.pos > 0)
                        chuck.Resize(part.pos);
                    else
                        chuck.Delete();
                }
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

            string chuckName;

            public Chuck(Chucks chucks, long id, long part, DB.Chuck row = null)
            {
                this.chucks = chucks;

                row = new DB.Chuck();
                row.ID = id;
                row.Part = part;

                if (row != null)
                    this.row = row;
                
                LoadRow();

                LastUsage = Static.UnixTimeMS;

                chuckName = row.ID.ToString("X") + "_" + row.Part.ToString("X") + ".bin";
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

            public void Close()
            {
                if (ramFile.Used)
                    MainFile().LinkRamFile(ramFile);

                if (fileSSD != null)
                    fileSSD.Close();

                if (fileHDD != null)
                    fileHDD.Close();



                chucks.RemoveChuck(row.ID, row.Part);
            }

            #endregion

            #region Drives

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
                if (SSDUpdated())
                {
                    onHDD = false;
                    return GetSSD();
                }
                else
                {
                    onHDD = true;
                    return GetHDD();
                }
            }

            public File MainFile()
            {
                File res = null;

                if (onHDD) 
                    res = fileHDD;
                else 
                    res = fileSSD;

                if (res == null)
                    res = BestDrive();

                return res;
            }

            public File GetSSD()
            {
                if(fileSSD == null)
                {
                    var drive = chucks.vhfs.AllDrives[row.SSD_ID];
                    fileSSD = new File(drive.Dir + chuckName, drive);
                }

                return fileSSD;
            }

            public File GetHDD()
            {
                if(fileHDD == null)
                {
                    var drive = chucks.vhfs.AllDrives[row.HDD_ID];
                    fileHDD = new File(drive.Dir + chuckName, drive);
                }

                return fileHDD;
            }

            public void IncreaseVersion()
            {
                if (onHDD && !HDDGreater())
                    row.HDD_Version += 1;

                if (!onHDD && !SSDGreater())
                    row.SSD_Version += 1;

                row.OnSSD = !onHDD;
            }

            public long CalculateAvgUsage()
            {
                // More precision to temperature to compensante integer flooring
                double temp = ((row.Temperature * Usages) + (chucks.TotalBytes.Avg * 100)) / (Usages + 1);
                row.Temperature = (long)temp;

                long now = Static.UnixTimeMS;

                double diff = now - LastUsage;
                AvgUsage = (diff + AvgUsage * Usages) / (Usages+1);

                Usages++;

                LastUsage = now;
                return now;
            }

            public void Sync(File file)
            {
                if (ramFile.Used)
                    file.LinkRamFile(ramFile);
            }

            #endregion

            public bool InOperation = false;
            public byte[] Read(Part part)
            {
                while (InOperation)
                    Thread.Yield();

                InOperation = true;

                var file = BestDrive();

                Sync(file);

                var res = file.Read(part.length, part.pos);

                row.LastRead = CalculateAvgUsage();
                InOperation = false;

                return res;
            }

            public void Write(Part part, byte[] bytes)
            {
                while (InOperation)
                    Thread.Yield();

                InOperation = true;
                inWrite = true;

                var file = BestDrive();
                bool writeToFile = true;

                if (onHDD)
                {
                    // If you are writing the entire file, write directly to SSD
                    if (part.pos == 0 && file.Length == part.length)
                    {
                        file = GetSSD();
                    }
                    else
                    {
                        ramFile.Write(bytes, part.pos);
                        writeToFile = false;
                    }

                    // Write the file on SSD
                    onHDD = false;
                }
                
                if(writeToFile)
                    file.Write(bytes, part.pos);

                row.LastWrite = CalculateAvgUsage();
                
                IncreaseVersion();

                InOperation = false;
            }

            public void Resize(long length)
            {
                var file = MainFile();

                file.drive.row.UsedBytes -= file.Length;
                file.drive.row.UsedBytes += length;

                file.Length = length;

                IncreaseVersion();
            }

            public void Delete()
            {
                if(row.SSD_Version >= 0)
                    GetSSD().Delete();

                if(row.HDD_Version >= 0)
                    GetHDD().Delete();
            }

            public void Dispose()
            {
                if (fileHDD != null)
                    fileHDD.Flush();

                if (fileSSD != null)
                    fileSSD.Flush();
            }

            #region Sync

            public void SyncVersion()
            {
                bool moveToHdd = row.SSD_Version > row.HDD_Version;

                if (moveToHdd && row.SSD_Version == row.HDD_Version)
                    return;

                File from = GetHDD();
                File to = GetSSD(); 

                if (moveToHdd)
                {
                    from = fileSSD;
                    to = fileSSD;
                }

                Sync(from);

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

            public void MoveToSSD(VHFS.Drive SSD = null)
            {
                if(SSD != null)
                    row.SSD_ID = SSD.id;

                SyncVersion();
                row.OnSSD = true;
            }

            #endregion
        }
    }
}
