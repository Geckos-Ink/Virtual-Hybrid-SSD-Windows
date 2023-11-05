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
            ///
            /// Chucks
            ///
            chucksUsage.Clear();

            foreach (var idChucks in vhfs.chucks.chucks)
            {
                foreach (var chuck in idChucks.Value)
                {
                    try
                    {
                        chucksUsage.Add(chuck.Value.LastUsage, chuck.Value);
                    }
                    catch { }
                }
            }

            var now = Static.UnixTimeMS;

            if (chucksUsage.Items.Count() > vhfs.Sets.maxOpenedChucks || (now - chucksUsage.Items.First().Key) > (vhfs.Sets.closeChuckAfter * 2))
            {
                foreach (var chuck in chucksUsage.Items)
                {
                    var diff = now - chuck.Key;
                    if (diff < vhfs.Sets.closeChuckAfter)
                        break;

                    if (chuck.Value.onExchange)
                        continue;

                    chuck.Value.Close();
                }
            }

            ///
            /// IterateStreams
            ///
            iterateStreamUsage.Clear();

            foreach(var stream in vhfs.DB.iterateStreams)
            {
                try
                {
                    iterateStreamUsage.Add(stream.lastChange, stream);
                }
                catch { }
            }

            foreach(var stream in iterateStreamUsage.Items)
            {
                if (now - stream.Key > vhfs.Sets.saveIterateStreamAfter)
                    break;

                stream.Value.Save();
            }
        }

        // Move most used and less used chucks
        const int maxMovingCycles = 25;
        const double maxSsdUsedSpace = 0.75;

        void ChucksOrderer()
        {
            while (true) {
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
                while(cycles < maxMovingCycles && ssdToFree.UsedSpace() > maxSsdUsedSpace)
                {
                    var indexes = orderedChucks.Items[cycles].Value;

                    // Yep, it doesn't count the cycles. This is the "Italian way"
                    foreach(var index in indexes) {
                        var row = vhfs.chucks.tableChuck.Get(index);
                        var chuck = vhfs.chucks.GetChuck(row);

                        // For the moment, in case of using instance, just ignore it
                        if (chuck.inUsing)
                            continue;

                        chuck.onExchange = true;
                    }

                    cycles++;
                }

                nextStep:
                    Thread.Sleep(10);
            }
        }

        ///
        /// Close
        ///

        public bool isClosing = false;

        void Close()
        {
            isClosing = true;

            foreach(var stream in vhfs.DB.iterateStreams)
            {
                stream.Save();
            }

            foreach (var idChucks in vhfs.chucks.chucks)
            {
                foreach (var chuck in idChucks.Value)
                {
                    chuck.Value.Close();
                }
            }

            foreach(var table in vhfs.DB.bytesTables)
            {
                table.Value.fileValues.Close();
            }

            foreach(var drive in vhfs.AllDrives)
            {
                drive.Close();
            }
        }
    }
}
