using System;
using System.Collections.Generic;
using System.Timers;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;

namespace TimerTriggeredCalc
{
    public static class Program
    {
        private static Timer aTimer;
        private static PIPoint output;
        private static PIPoint input;

        public static void Main(string[] args)
        {
            #region configuration

            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_timerbased";
            var timerMS = 60000; // how long to pause between cycles, in ms

            #endregion // configuration

            // Get default PI Data Archive
            var myServer = PIServers.GetPIServers().DefaultPIServer;

            // Get or create the output PI Point
            try
            {
                output = PIPoint.FindPIPoint(myServer, outputTagName);
            }
            catch (PIPointInvalidException)
            {
                output = myServer.CreatePIPoint(outputTagName);
                output.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
            }

            // Resolve input tag name to PIPoint object
            input = PIPoint.FindPIPoint(myServer, inputTagName);

            // Create a timer with the specified interval
            aTimer = new Timer();
            aTimer.Interval = timerMS;

            // Add the calculation to the timer's elapsed trigger event handler list
            aTimer.Elapsed += PerformCalculation;

            // Enable the timer and have it reset on each trigger
            aTimer.AutoReset = true;
            aTimer.Enabled = true;

            // Allow the program to run indefinitely
            Console.WriteLine($"Calculations are executing every {timerMS} ms. Press <ENTER> to end... ");
            Console.ReadLine();

        }

        private static void PerformCalculation(object source, ElapsedEventArgs e)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow

            // Obtain the recent values from the trigger timestamp
            var afvals = input.RecordedValuesByCount(e.SignalTime, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            var badItems = new List<AFValue>();
            foreach (var afval in afvals)
                if (!afval.IsGood)
                    badItems.Add(afval);

            foreach (var item in badItems)
                afvals.Remove(item);

            // Loop until no new values were eliminated for being outside of the boundaries
            while (true)
            {
                // Calculate the mean
                var total = 0.0;
                foreach (var afval in afvals)
                    total += afval.ValueAsDouble();

                var avg = total / (double)afvals.Count;

                // Calculate the st dev
                var totalSquareVariance = 0.0;
                foreach (var afval in afvals)
                    totalSquareVariance += Math.Pow(afval.ValueAsDouble() - avg, 2);

                var avgSqDev = totalSquareVariance / (double)afvals.Count;
                var stdev = Math.Sqrt(avgSqDev);

                // Determine the values outside of the boundaries
                var cutoff = stdev * numStDevs;
                var itemsToRemove = new List<AFValue>();

                foreach (var afval in afvals)
                    if (Math.Abs(afval.ValueAsDouble() - avg) > cutoff)
                        itemsToRemove.Add(afval);

                // If there are items to remove, remove them and loop again
                if (itemsToRemove.Count > 0)
                {
                    foreach (var item in itemsToRemove)
                        afvals.Remove(item);
                }
                // If not, write the average to the output tag and break the loop
                else
                {
                    output.UpdateValue(new AFValue(avg, e.SignalTime), AFUpdateOption.Insert);
                    break;
                }

            }
        }
    }
}
