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
using OSIsoft.AF.PI;
using Timer = System.Timers.Timer;

namespace TimerTriggeredCalc
{
    public static class Program
    {
        private static readonly List<CalculationContextResolved> _contextListResolved = new List<CalculationContextResolved>();
        private static Timer _aTimer;
        private static Exception _toThrow;

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
                Console.WriteLine("Resolving PI Data Archive object...");

                PIServer myServer;

                if (string.IsNullOrWhiteSpace(settings.PIDataArchiveName))
                {
                    myServer = PIServers.GetPIServers().DefaultPIServer;
                }
                else
                {
                    myServer = PIServers.GetPIServers()[settings.PIDataArchiveName];
                }
                #endregion // step1

                #region step2
                Console.WriteLine("Resolving input and output PIPoint objects...");

                foreach (var context in settings.CalculationContexts)
                {
                    var thisResolvedContext = new CalculationContextResolved();

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

                            // Turn off compression, set to Double, and confirm there were no errors in doing so
                            thisResolvedContext.OutputTag.SetAttribute(PICommonPointAttributes.Compressing, 0);
                            thisResolvedContext.OutputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
                            var errors = thisResolvedContext.OutputTag.SaveAttributes(PICommonPointAttributes.Compressing,
                                                                                      PICommonPointAttributes.PointType);

                            if (errors != null && errors.HasErrors)
                            {
                                Console.WriteLine("Errors calling PIPoint.SaveAttributes:");
                                foreach (var item in errors.Errors)
                                {
                                    Console.WriteLine($"  {item.Key}: {item.Value}");
                                }

                                throw new Exception("Error saving Output PIPoint configuration changes");
                            }
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
                #endregion // step2

                #region step3
                Console.WriteLine("Creating a timer to trigger the calculations...");

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
                Console.WriteLine("Triggering the initial calculation...");

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
            PerformAllCalculations(e.SignalTime);
        }

        /// <summary>
        /// Wrapper function that abstracts the iteration of contexts from the calculation logic itself
        /// </summary>
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        private static void PerformAllCalculations(DateTime triggerTime)
        {
            foreach (var context in _contextListResolved)
            {
                PerformCalculation(triggerTime, context);
            }
        }

        /// <summary>
        /// This function performs the calculation and writes the value to the output tag
        /// </summary>
        /// <param name="triggerTime">The timestamp to perform the calculation against</param>
        /// <param name="context">The context on which to perform this calculation</param>
        private static void PerformCalculation(DateTime triggerTime, CalculationContextResolved context)
        {
            // Configuration
            var numValues = 100;  // number of values to find the average of
            var numStDevs = 1.75; // number of standard deviations of variance to allow

            // Obtain the recent values from the trigger timestamp
            var afvals = context.InputTag.RecordedValuesByCount(triggerTime, numValues, false, AFBoundaryType.Interpolated, null, false);

            // Remove bad values
            afvals.RemoveAll(afval => !afval.IsGood);

            // Loop until no new values were eliminated for being outside of the boundaries
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
                    var cutoff = stdev * numStDevs;
                    var startingCount = afvals.Count;

                    afvals.RemoveAll(afval => Math.Abs(afval.ValueAsDouble() - mean) > cutoff);

                    // If no items were removed, output the average and break the loop
                    if (afvals.Count == startingCount)
                    {
                        context.OutputTag.UpdateValue(new AFValue(mean, triggerTime), AFUpdateOption.Insert);
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
