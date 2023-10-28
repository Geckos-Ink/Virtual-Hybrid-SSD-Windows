using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Chucks
    {
        VHFS vhfs;

        public Chucks(VHFS vhfs)
        {
            this.vhfs = vhfs;
        }

        public class Chuck
        {
            Chucks chucks;

            public Chuck(Chucks chucks)
            {
                this.chucks = chucks;
            }
        }
    }
}
