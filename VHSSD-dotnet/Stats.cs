using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VHSSD
{
    public class Stats
    {
        double val;
        double peak;

        long lastSecond = 0;
        double accumuledVals = 0;

        Stats appendTo;
        public Stats(Stats appendTo = null)
        {
            this.appendTo = appendTo;
        }

        public void Add(double val)
        {
            if (appendTo != null)
                appendTo.Add(val);

            var now = Static.UnixTime;
            if(lastSecond != now)
            {
                val = accumuledVals;
                accumuledVals = 0;
                lastSecond = now;

                calcAvg();
            }

            accumuledVals += val;
        }

        double avg = 0;
        double avgPeak = 0;

        void calcAvg()
        {
            if (peak < val) peak = val;

            avg = (avg + val) / 2;

            if(avgPeak < avg) avgPeak = avg;
        }

        #region Properties

        public double Val
        {
            get
            {
                return val;
            }
        }

        public double Peak
        {
            get
            {
                return peak;
            }
        }

        public double Avg
        {
            get
            {
                return avg;
            }
        }

        public double AvgPeak
        {
            get
            {
                return avgPeak;
            }
        }

        #endregion
    }
}
