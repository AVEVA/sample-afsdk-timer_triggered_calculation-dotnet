using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using Timer = System.Timers.Timer;

namespace TimerTriggeredCalc
{
    public static class Program
    {
        private static Timer _aTimer;
        private static PIPoint _output;
        private static PIPoint _input;

        public static void Main()
        {
            var success = MainLoop(false);
        }

        public static bool MainLoop(bool test = false)
        {
            #region configuration

            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_timerbased";
            var timerMS = 60000; // how long to pause between cycles, in ms
            var startOnTheMinute = true; // start the calculation exactly on the minute

            #endregion // configuration

            // Get default PI Data Archive
            var myServer = PIServers.GetPIServers().DefaultPIServer;

            // Get or create the output PI Point
            try
            {
                _output = PIPoint.FindPIPoint(myServer, outputTagName);
            }
            catch (PIPointInvalidException)
            {
                _output = myServer.CreatePIPoint(outputTagName);
                _output.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
            }

            // Resolve input tag name to PIPoint object
            _input = PIPoint.FindPIPoint(myServer, inputTagName);

            // Optionally pause the program until the top of the next minute
            if (startOnTheMinute)
            {
                DateTime now = DateTime.Now;
                Thread.Sleep(((60 - now.Second) * 1000) - now.Millisecond);
            }

            // Create a timer with the specified interval
            _aTimer = new Timer();
            _aTimer.Interval = timerMS;

            // Add the calculation to the timer's elapsed trigger event handler list
            _aTimer.Elapsed += PerformCalculation;

            // Enable the timer and have it reset on each trigger
            _aTimer.AutoReset = true;
            _aTimer.Enabled = true;

            // Allow the program to run indefinitely if not being tested
            if (!test)
            {
                Console.WriteLine($"Calculations are executing every {timerMS} ms. Press <ENTER> to end... ");
                Console.ReadLine();
            }
            else
            {
                // Pause to let the calculation run twice before ending the test
                Thread.Sleep(timerMS * 2);
            }

            return true;
        }

        private static void PerformCalculation(object source, ElapsedEventArgs e)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow

            // Obtain the recent values from the trigger timestamp
            var afvals = _input.RecordedValuesByCount(e.SignalTime, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            var badItems = new List<AFValue>();
            foreach (var afval in afvals)
            {
                if (!afval.IsGood)
                {
                    badItems.Add(afval);
                }
            }

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
                {
                    if (Math.Abs(afval.ValueAsDouble() - avg) > cutoff)
                    {
                        itemsToRemove.Add(afval);
                    }
                }

                // If there are items to remove, remove them and loop again
                if (itemsToRemove.Count > 0)
                {
                    foreach (var item in itemsToRemove)
                        afvals.Remove(item);
                }

                // If not, write the average to the output tag and break the loop
                else
                {
                    _output.UpdateValue(new AFValue(avg, e.SignalTime), AFUpdateOption.Insert);
                    break;
                }
            }
        }
    }
}
