using System;
using System.Threading;
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

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            var success = MainLoop(false);
        }

        /// <summary>
        /// This function loops until manually stopped, triggering the calculation event on the prescribed timer.
        /// If being tested, it stops after the set amount of time
        /// </summary>
        /// <param name="test">Whether the function is running a test or not</param>
        /// <returns>true if successful</returns>
        public static bool MainLoop(bool test = false)
        {
            #region configuration
            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_timerbased";
            var timerMS = 60 * 1000; // how long to pause between cycles, in ms
            var startOnTheMinute = true; // start the calculation exactly on the minute

            #endregion // configuration

            // Get PI Data Archive object

            // Default server
            var myServer = PIServers.GetPIServers().DefaultPIServer;

            // Named server
            // var dataArchiveName = "PISRV01";
            // var myServer = PIServers.GetPIServers()[dataArchiveName];

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
            _aTimer.Elapsed += TriggerCalculation;

            // Enable the timer and have it reset on each trigger
            _aTimer.AutoReset = true;
            _aTimer.Enabled = true;

            // Once the timer is set up, trigger the calculation manually to not wait a full timer cycle
            PerformCalculation(DateTime.Now);

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

        /// <summary>
        /// This function triggers the calculation to be run against the timestamp of the timer event
        /// </summary>
        /// <param name="source">The source of the event</param>
        /// <param name="e">An ElapsedEventArgs object that contains the event data</param>
        private static void TriggerCalculation(object source, ElapsedEventArgs e)
        {
            PerformCalculation(e.SignalTime);
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// </summary>
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        private static void PerformCalculation(DateTime triggerTime)
        { 
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow

            // Obtain the recent values from the trigger timestamp
            var afvals = _input.RecordedValuesByCount(triggerTime, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            afvals.RemoveAll(a => !a.IsGood);
            
            // Loop until no new values were eliminated for being outside of the boundaries
            while (true)
            {
                var avg = 0.0;

                if (afvals.Count > 0)
                {
                    // Calculate the mean
                    var total = 0.0;
                    foreach (var afval in afvals)
                    {
                        total += afval.ValueAsDouble();
                    }

                    avg = total / afvals.Count;

                    // Calculate the st dev
                    var totalSquareVariance = 0.0;
                    foreach (var afval in afvals)
                    {
                        totalSquareVariance += Math.Pow(afval.ValueAsDouble() - avg, 2);
                    }

                    var avgSqDev = totalSquareVariance / (double)afvals.Count;
                    var stdev = Math.Sqrt(avgSqDev);

                    // Determine the values outside of the boundaries, and remove them
                    var cutoff = stdev * numStDevs;
                    var startingCount = afvals.Count;

                    afvals.RemoveAll(a => Math.Abs(a.ValueAsDouble() - avg) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        _output.UpdateValue(new AFValue(avg, triggerTime), AFUpdateOption.Insert);
                        break;
                    }
                }
                else
                {
                    _output.UpdateValue(new AFValue(avg, triggerTime), AFUpdateOption.Insert);
                    break;
                }
            }
        }
    }
}
