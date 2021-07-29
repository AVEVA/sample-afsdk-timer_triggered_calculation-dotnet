using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
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
        private static List<CalculationContextResolved> _contextListResolved = new List<CalculationContextResolved>();
        private static Exception _toThrow;

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            MainLoop(false);
        }

        /// <summary>
        /// This function loops until manually stopped, triggering the calculation event on the prescribed timer.
        /// If being tested, it stops after the set amount of time
        /// </summary>
        /// <param name="test">Whether the function is running a test or not</param>
        /// <returns>true if successful</returns>
        public static bool MainLoop(bool test = false)
        {
            try
            {
                #region configurationSettings
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Directory.GetCurrentDirectory() + "/appsettings.json"));
                #endregion // configurationSettings

                // Get PI Data Archive object
                PIServer myServer;

                if (string.IsNullOrWhiteSpace(settings.PIDataArchiveName))
                {
                    myServer = PIServers.GetPIServers().DefaultPIServer;
                }
                else
                {
                    myServer = PIServers.GetPIServers()[settings.PIDataArchiveName];
                }

                // Resolve the input and output tag names to PIPoint objects
                foreach (var context in settings.CalculationContexts)
                {
                    CalculationContextResolved thisResolvedContext = new CalculationContextResolved();

                    try
                    {
                        // Resolve the input PIPoint object from its name
                        thisResolvedContext.InputTag = PIPoint.FindPIPoint(myServer, context.InputTagName);

                        try
                        {
                            // Try to resolve the output PIPoint object from its name
                            thisResolvedContext.OutputTag = PIPoint.FindPIPoint(myServer, context.OutputTagName);
                        }
                        catch (PIPointInvalidException)
                        {
                            // If it does not exist, create it
                            thisResolvedContext.OutputTag = myServer.CreatePIPoint(context.OutputTagName);
                            thisResolvedContext.OutputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
                        }

                        // If this was successful, add this context pair to the list of resolved contexts
                        _contextListResolved.Add(thisResolvedContext);
                    }
                    catch (Exception ex)
                    {
                        // If not successful, inform the user and move on to the next pair
                        Console.WriteLine($"Input tag {context.InputTagName} will be skipped due to error: {ex.Message}");
                    }
                }

                // Create a timer with the specified interval
                _aTimer = new Timer();
                _aTimer.Interval = settings.TimerIntervalMS;

                // Add the calculation to the timer's elapsed trigger event handler list
                _aTimer.Elapsed += TriggerCalculation;

                // Optionally pause the program until the specified offset
                if (settings.DefineOffsetSeconds)
                {
                    DateTime now = DateTime.Now;
                    var secondsUntilOffset = (60 + (settings.OffsetSeconds - now.Second)) % 60;
                    Thread.Sleep((secondsUntilOffset * 1000) - now.Millisecond);
                }

                // Enable the timer and have it reset on each trigger
                _aTimer.AutoReset = true;
                _aTimer.Enabled = true;

                // Once the timer is set up, trigger the calculation manually to not wait a full timer cycle
                PerformCalculation(DateTime.Now);

                // Allow the program to run indefinitely if not being tested
                if (!test)
                {
                    Console.WriteLine($"Calculations are executing every {settings.TimerIntervalMS} ms. Press <ENTER> to end... ");
                    Console.ReadLine();
                }
                else
                {
                    // Pause to let the calculation run twice before ending the test
                    Thread.Sleep(settings.TimerIntervalMS * 2);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                _toThrow = ex;
                throw;
            }
            finally
            {
                // Dispose the timer object then quit
                if (_aTimer != null)
                {
                    Console.WriteLine("Disposing timer...");
                    _aTimer.Dispose();
                }
            }
                
            Console.WriteLine("Quitting...");
            return _toThrow == null;
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

            foreach (var context in _contextListResolved)
            {
                // Obtain the recent values from the trigger timestamp
                var afvals = context.InputTag.RecordedValuesByCount(triggerTime, numValues, false, AFBoundaryType.Interpolated, null, false);

                // Remove bad values
                afvals.RemoveAll(afval => !afval.IsGood);

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

                        afvals.RemoveAll(afval => Math.Abs(afval.ValueAsDouble() - avg) > cutoff);

                        // If no items were removed, output the average and break the loop
                        if (afvals.Count == startingCount)
                        {
                            context.OutputTag.UpdateValue(new AFValue(avg, triggerTime), AFUpdateOption.Insert);
                            break;
                        }
                    }
                    else
                    {
                        // If all of the values have been removed, don't write any output values
                        Console.WriteLine($"All values were eliminated from the set. No output will be written to {context.OutputTag.Name} for {triggerTime}.");
                        break;
                    }
                }
            }
        }
    }
}
