using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using Timer = System.Timers.Timer;

namespace TimerTriggeredCalc
{
    public static class Program
    {
        private static readonly List<AFElement> _contextList = new List<AFElement>();
        private static AFDataCache _myAFDataCache;
        private static AFKeyedResults<AFAttribute, AFData> _dataCaches;
        private static Exception _toThrow;
        private static Timer _aTimer;

        /// <summary>
        /// Entry point of the program
        /// </summary>
        public static void Main()
        {
            // Create a cancellation token source in order to cancel the calculation loop on demand
            var source = new CancellationTokenSource();
            var token = source.Token;

            // Launch the sample's main loop, passing it the cancellation token
            var success = MainLoop(token);

            // Pause until the user decides to end the loop
            Console.WriteLine($"Press <ENTER> to end... ");
            Console.ReadLine();

            // Cancel the operation and wait until everything is canceled properly
            source.Cancel();
            _ = success.Result;

            // Dispose of the cancellation token source and exit the program
            if (source != null)
            {
                Console.WriteLine("Disposing cancellation token source...");
                source.Dispose();
            }

            Console.WriteLine("Quitting Main...");
        }

        /// <summary>
        /// This function loops until manually stopped, triggering the calculation event on the prescribed timer.
        /// If being tested, it stops after the set amount of time
        /// </summary>
        /// <param name="token">Controls if the loop should stop and exit</param>
        /// <returns>true if successful</returns>
        public static async Task<bool> MainLoop(CancellationToken token)
        {
            try
            {
                #region configurationSettings
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Directory.GetCurrentDirectory() + "/appsettings.json"));
                #endregion // configurationSettings

                #region step1
                Console.WriteLine("Resolving AF Server object...");

                var myPISystems = new PISystems();
                PISystem myPISystem;

                if (string.IsNullOrWhiteSpace(settings.AFServerName))
                {
                    // Use the default PI Data Archive
                    myPISystem = myPISystems.DefaultPISystem;
                }
                else
                {
                    myPISystem = myPISystems[settings.AFServerName];
                }

                Console.WriteLine("Resolving AF Database object...");

                AFDatabase myAFDB;

                if (string.IsNullOrWhiteSpace(settings.AFDatabaseName))
                {
                    // Use the default PI Data Archive
                    myAFDB = myPISystem.Databases.DefaultDatabase;
                }
                else
                {
                    myAFDB = myPISystem.Databases[settings.AFDatabaseName];
                }
                #endregion // step1

                #region step2
                Console.WriteLine("Resolving AFAttributes to add to the Data Cache...");

                var attributeCacheList = new List<AFAttribute>();
                
                // Resolve the input and output tag names to PIPoint objects
                foreach (var context in settings.Contexts)
                {
                    try
                    {
                        // Resolve the element from its name
                        var thisElement = myAFDB.Elements[context];

                        // Make a list of inputs to ensure a partially failed context resolution doesn't add to the data cache
                        var thisattributeCacheList = new List<AFAttribute>();

                        // Resolve each input attribute
                        foreach (var input in settings.Inputs)
                        {
                            thisattributeCacheList.Add(thisElement.Attributes[input]);
                        }

                        // If successful, add to the list of resolved attributes to the data cache list
                        _contextList.Add(thisElement);
                        attributeCacheList.AddRange(thisattributeCacheList);
                    }
                    catch (Exception ex)
                    {
                        // If not successful, inform the user and move on to the next pair
                        Console.WriteLine($"Context {context} will be skipped due to error: {ex.Message}");
                    }
                }
                #endregion // step2

                #region step3
                Console.WriteLine("Creating a data cache for snapshot event updates...");

                _myAFDataCache = new AFDataCache();
                _dataCaches = _myAFDataCache.Add(attributeCacheList);
                _myAFDataCache.CacheTimeSpan = new TimeSpan(settings.CacheTimeSpanSeconds * TimeSpan.TicksPerSecond);

                // Create a timer with the specified interval of checking for updates
                _aTimer = new Timer()
                {
                    Interval = settings.TimerIntervalMS,
                    AutoReset = true,
                };

                // Add the calculation to the timer's elapsed trigger event handler list
                _aTimer.Elapsed += TriggerCalculation;
                #endregion // step3

                #region step4
                if (settings.DefineOffsetSeconds)
                {
                    Console.WriteLine($"Pausing until the defined offset of {settings.OffsetSeconds} seconds...");
                    var now = DateTime.Now;
                    var secondsUntilOffset = (60 + (settings.OffsetSeconds - now.Second)) % 60;
                    Thread.Sleep((secondsUntilOffset * 1000) - now.Millisecond);
                }
                else
                {
                    Console.WriteLine("Not pausing until a define offset");
                }

                // Enable the timer and have it reset on each trigger
                _aTimer.Enabled = true;
                #endregion // step4

                #region step5
                Console.WriteLine("Triggering the initial calculation...");
                
                PerformAllCalculations(DateTime.Now);
                #endregion // step5

                #region step6
                Console.WriteLine("Waiting for cancellation token to be triggered...");

                await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                #endregion //step6
            }
            catch (TaskCanceledException)
            {
                // Task cancellation is done via exception but shouldn't denote a failure
                Console.WriteLine("Task canceled successfully");
            }
            catch (Exception ex)
            {
                // All other exceptions should be treated as a failure
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
            _myAFDataCache.UpdateData();
            PerformAllCalculations(e.SignalTime);
        }

        /// <summary>
        /// Wrapper function that abstracts the iteration of contexts from the calculation logic itself
        /// </summary>
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        private static void PerformAllCalculations(DateTime triggerTime)
        {
            foreach (var context in _contextList)
            {
                PerformCalculation(triggerTime, context);
            }
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        /// <param name="context">The context on which to perform this calculation</param>
        private static void PerformCalculation(DateTime triggerTime, AFElement context)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var forward = false;
            var tempUom = "K";
            var pressUom = "torr";
            var volUom = "L";
            var molUom = "mol";
            var filterExpression = string.Empty;
            var includeFilteredValues = false;

            var numStDevs = 1.75; // number of standard deviations of variance to allow

            // Obtain the recent values from the trigger timestamp
            var afTempVals = context.Attributes["Temperature"].Data.RecordedValuesByCount(triggerTime, numValues, forward, AFBoundaryType.Interpolated, context.PISystem.UOMDatabase.UOMs[tempUom], filterExpression, includeFilteredValues);
            var afPressVals = context.Attributes["Pressure"].Data.RecordedValuesByCount(triggerTime, numValues, forward, AFBoundaryType.Interpolated, context.PISystem.UOMDatabase.UOMs[pressUom], filterExpression, includeFilteredValues);
            var afVolumeVal = context.Attributes["Volume"].Data.EndOfStream(context.PISystem.UOMDatabase.UOMs[volUom]);

            // Remove bad values
            afTempVals.RemoveAll(afval => !afval.IsGood);
            afPressVals.RemoveAll(afval => !afval.IsGood);

            // Iteratively solve for the trimmed mean of temperature and pressure
            var meanTemp = GetTrimmedMean(afTempVals, numStDevs);
            var meanPressure = GetTrimmedMean(afPressVals, numStDevs);

            // Apply the Ideal Gas Law (PV = nRT) to solve for number of moles
            var gasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            var n = meanPressure * afVolumeVal.ValueAsDouble() / (gasConstant * meanTemp); // PV = nRT; n = PV/(RT)

            // write to output attribute.
            context.Attributes["Moles"].Data.UpdateValue(new AFValue(n, triggerTime, context.PISystem.UOMDatabase.UOMs[molUom]), AFUpdateOption.Insert);
        }

        private static double GetTrimmedMean(AFValues afvals, double numberOfStandardDeviations)
        {
            while (true)
            {
                // Don't loop if all values have been removed
                if (afvals.Count > 0)
                {
                    // Calculate the mean
                    var total = 0.0;
                    foreach (var afval in afvals)
                    {
                        total += afval.ValueAsDouble();
                    }

                    var mean = total / afvals.Count;

                    // Calculate the standard deviation
                    var totalSquareVariance = 0.0;
                    foreach (var afval in afvals)
                    {
                        totalSquareVariance += Math.Pow(afval.ValueAsDouble() - mean, 2);
                    }

                    var meanSqDev = totalSquareVariance / (double)afvals.Count;
                    var stdev = Math.Sqrt(meanSqDev);

                    // Determine the values outside of the boundaries, and remove them
                    var cutoff = stdev * numberOfStandardDeviations;
                    var startingCount = afvals.Count;

                    afvals.RemoveAll(afval => Math.Abs(afval.ValueAsDouble() - mean) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        return mean;
                    }
                }
                else
                {
                    throw new Exception("All values were eliminated. No mean could be calculated");
                }
            }
        }
    }
}
