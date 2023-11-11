using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Debug
    {
        public void Write(string[] args)
        {
            string res = Static.UnixTime + ":";

            foreach (var a in args)
                res += " \t" + a;

            Console.WriteLine(res);
        }
    }
}
