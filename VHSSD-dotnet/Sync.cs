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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static VHSSD.Chucks;

namespace VHSSD
{
    public class Sync
    {
        VHFS vhfs;

        bool timerDisposeActive = false;
        bool chucksOrdererActive = false;

        Timer timerDispose;
        Thread chucksOrdererThread;

        public Sync(VHFS vhfs)
        {
            this.vhfs = vhfs;

            timerDispose = new Timer(TimerDispose, null, 0, 1000);

            chucksOrdererThread = new Thread(ChucksOrderer);
            chucksOrdererThread.Start();
        }

        OrderedDictionary<long, Chuck> chucksUsage = new OrderedDictionary<long, Chuck>();
        OrderedDictionary<long, DB.IterateStream> iterateStreamUsage = new OrderedDictionary<long, DB.IterateStream>();

        public void TimerDispose(object state)
        {
            if (isClosing || timerDisposeActive) return;

            timerDisposeActive = true;

            var now = Static.UnixTimeMS;

            ///
            /// Chucks
            ///

            try
            {
                chucksUsage.Clear();

                var chucksByFile = new Dictionary<long, Dictionary<long, Chuck>>(vhfs.chucks.chucks);
                foreach (var idChucks in chucksByFile)
                {
                    var chucks = new Dictionary<long, Chuck>(idChucks.Value);
                    foreach (var chuck in chucks)
                    {
                        try
                        {
                            chucksUsage.Add(chuck.Value.LastUsage, chuck.Value);
                        }
                        catch { }
                    }
                }

                if (chucksUsage.Items.Count > 0)
                {
                    if (chucksUsage.Items.Count() > vhfs.Sets.maxOpenedChucks || (now - chucksUsage.Items.First().Key) > (vhfs.Sets.closeChuckAfter * 2))
                    {
                        foreach (var chuck in chucksUsage.Items)
                        {
                            var diff = now - chuck.Key;
                            if (diff < vhfs.Sets.closeChuckAfter)
                                break;

                            var cv = chuck.Value;
                            if (cv.InOperation || cv.onExchange)
                                continue;

                            cv.Close();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            ///
            /// IterateStreams
            ///

            try
            {
                iterateStreamUsage.Clear();

                var _iterateStreams = new List<DB.IterateStream>(vhfs.DB.iterateStreams);
                foreach (var stream in _iterateStreams)
                {
                    try
                    {
                        iterateStreamUsage.Add(stream.lastChange, stream);
                    }
                    catch { }
                }

                foreach (var stream in iterateStreamUsage.Items)
                {
                    if (now - stream.Key > vhfs.Sets.saveIterateStreamAfter)
                        break;

                    stream.Value.Save();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            timerDisposeActive = false;
        }

        // Move most used and less used chucks
        const int maxMovingCycles = 25;
        const double maxSsdUsedSpace = 0.75;

        void ChucksOrderer()
        {
            while (true) {

                if (isClosing) return;

                try
                {
                    chucksOrdererActive = true;

                    ///
                    /// Free SSD
                    ///
                    var ssdsToFree = new List<VHFS.Drive>();

                    foreach (var drive in vhfs.SSDDrives)
                    {
                        var fs = drive.UsedSpace();
                        if (fs > maxSsdUsedSpace)
                            ssdsToFree.Add(drive);
                    }

                    if (ssdsToFree.Count == 0)
                        goto nextStep;

                    var ssdToFree = ssdsToFree.OrderBy(d => d.lastUsedSpace).First();

                    // Break the glass in case of necessity
                    //var hddUsages = vhfs.HDDDrives.OrderBy(d => d.lastUsedSpace);
                    //var lessUsedHDD = hddUsages.Last();

                    var where = new DB.Chuck() { OnSSD = true, SSD_ID = ssdToFree.id };
                    var orderedChucks = vhfs.chucks.tableChuck.AvgKeys("Temperature", "LastUsage", where);

                    var cycles = 0;
                    while (cycles < maxMovingCycles && ssdToFree.UsedSpace() > maxSsdUsedSpace)
                    {
                        var indexes = orderedChucks.Items[cycles].Value;

                        // Yep, it doesn't count the cycles. This is the "Italian way"
                        foreach (var index in indexes)
                        {
                            var row = vhfs.chucks.tableChuck.Get(index);
                            var chuck = vhfs.chucks.GetChuck(row);

                            // For the moment, in case of using instance, just ignore it
                            if (chuck.inUsing)
                                continue;

                            chuck.onExchange = true;
                            chuck.MoveToHDD();
                            chuck.onExchange = false;
                        }

                        cycles++;
                    }

                nextStep:
                    ///
                    /// Move warmer files to SSD
                    ///
                    var mostFreeSsdsList = new List<VHFS.Drive>();

                    foreach (var drive in vhfs.SSDDrives)
                    {
                        var fs = drive.UsedSpace();
                        if (fs < 0.75)
                            mostFreeSsdsList.Add(drive);
                    }

                    var mostFree = mostFreeSsdsList.OrderByDescending(d => d.lastUsedSpace).ToList();
                    var warmerChucks = vhfs.chucks.tableChuck.AvgKeys("Temperature", "LastUsage");

                    var keyTemperature = vhfs.chucks.tableChuck.GetKey("Temperature").GetOrderedKeys();
                    var avgTemp = keyTemperature.Avg();

                    var tick = 0;
                    cycles = 0; // little bit confusing? but i'm a really confusing. 
                    while (cycles < maxMovingCycles && tick < warmerChucks.Items.Count && mostFree.Count() > 0)
                    {
                        var indexes = warmerChucks.Items[warmerChucks.Items.Count - (1 + tick)];

                        foreach (var index in indexes.Value)
                        {
                            var row = vhfs.chucks.tableChuck.Get(index);
                            if (row.OnSSD) continue;

                            if (row.Temperature < avgTemp) continue;

                            var chuck = vhfs.chucks.GetChuck(row);
                            if (chuck.inUsing) continue;

                            var ssd = mostFree[cycles % mostFree.Count];
                            chuck.MoveToSSD();

                            cycles++;
                        }

                        tick++;
                    }

                    // Save iterate streams if needed
                    var _iterateStreams = new List<DB.IterateStream>(vhfs.DB.iterateStreams);
                    foreach (var stream in _iterateStreams)
                    {
                        if (stream.Changed && (Static.UnixTimeMS - stream.lastChange) > 1000)
                        {
                            stream.Save();
                            stream.Changed = false;
                        }
                    }
                }
                catch (Exception ex) 
                { 
                    Console.WriteLine(ex.ToString());
                }

                chucksOrdererActive = false;

                // Wait (a little) for it...
                Thread.Sleep(10);
            }
        }

        public bool IsActive
        {
            get
            {
                return timerDisposeActive || chucksOrdererActive;
            }
        }

        ///
        /// Close
        ///

        public bool isClosing = false;

        public void Close()
        {
            isClosing = true;

            while (IsActive)
                Thread.Sleep(1);

            while (isClosing)
            {
                try
                {
                    // Close chucks
                    foreach (var idChucks in vhfs.chucks.chucks)
                    {
                        foreach (var chuck in idChucks.Value)
                        {
                            chuck.Value.Close();
                        }
                    }

                    // Close all drives
                    foreach (var drive in vhfs.AllDrives)
                    {
                        drive.Close();
                    }

                    ///
                    /// Don't close anything (afterall that saves data) after these objects
                    ///

                    // Save iterate streams
                    var _iterateStreams = new List<DB.IterateStream>(vhfs.DB.iterateStreams);
                    foreach (var stream in _iterateStreams)
                    {
                        stream.Save();
                    }

                    // Close bytes tables
                    foreach (var table in vhfs.DB.bytesTables)
                    {
                        table.Value.fileValues?.Close();
                    }

                    isClosing = false;
                }
                catch (Exception ex) 
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}
