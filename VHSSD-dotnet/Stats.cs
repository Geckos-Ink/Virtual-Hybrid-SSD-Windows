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
