using System;
using System.Collections.Generic;
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

        public Sync(VHFS vhfs)
        {
            this.vhfs = vhfs;

            timerDispose = new Timer(TimerDispose, null, 0, 1000);
        }

        OrderedDictionary<long, Chuck> chucksUsage = new OrderedDictionary<long, Chuck>();

        public void TimerDispose(object state)
        {
            chucksUsage.Clear();

            foreach (var idChucks in vhfs.chucks.chucks)
            {
                foreach (var chuck in idChucks.Value)
                {
                    chucksUsage.Add(chuck.Value.LastUsage, chuck.Value);
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

                    chuck.Value.Close();
                }
            }
        }
    }
}
