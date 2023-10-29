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

        DB.Table<DB.Chuck> tableChuck;

        Dictionary<long, Dictionary<long, Chuck>> chucks = new Dictionary<long, Dictionary<long, Chuck>>();

        public Chucks(VHFS vhfs)
        {
            this.vhfs = vhfs;

            tableChuck = vhfs.DB.GetTable<DB.Chuck>();
            tableChuck.SetKey("ID", "Part");
        }

        public byte[] Read(long ID, long pos, long length)
        {
            return null;
        }

        public class Chuck
        {
            Chucks chucks;

            DB.Chuck chuckRow;

            File fileHDD;
            File fileSSD;

            byte[] data;

            public Chuck(Chucks chucks, long id, long part)
            {
                this.chucks = chucks;

                chuckRow = new DB.Chuck();
                chuckRow.ID = id;
                chuckRow.Part = part;

                LoadRow();
            }

            public void LoadRow()
            {
                var row = chucks.tableChuck.Get(chuckRow, "ID,Part");
                if (row == null) return;
                chuckRow = row;
            }

            public void LoadFile()
            {

            }
        }
    }
}
