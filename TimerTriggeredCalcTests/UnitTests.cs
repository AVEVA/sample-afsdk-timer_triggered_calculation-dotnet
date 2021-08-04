using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using TimerTriggeredCalc;
using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using Xunit;


namespace TimerTriggeredCalcTests
{
    public class UnitTests
    {
        [Fact]
        public void TimerTriggeredCalcTest()
        {
            double valToWrite = 0.0;
            int numValsToWrite = 3; // must be 2 or more to check for proper interval spacing
            var errorThreshold = new TimeSpan(0, 0, 0, 1); // 1s time max error is acceptable due to race condition between sample and test
            var cancellationThreshold = 15; // canceling the timer object is slightly unpredictable, don't fail the test unless it loops far too many times

            try
            {
                #region configurationSettings
                string solutionFolderName = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName;
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(solutionFolderName + "/TimerTriggeredCalc/appsettings.json"));
                #endregion // configurationSettings

                #region step1
                Console.WriteLine("TEST: Resolving PI Data Archive object...");

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
                Console.WriteLine("TEST: Resolving input and output PIPoint objects...");

                var contextListResolved = new List<CalculationContextResolvedTest>();

                foreach (var context in settings.CalculationContexts)
                {
                    var thisResolvedContext = new CalculationContextResolvedTest();

                    try
                    {
                        // Resolve the input PIPoint object from its name, ensuring it does not already exist
                        try
                        {
                            thisResolvedContext.InputTag = PIPoint.FindPIPoint(myServer, context.InputTagName);
                            Assert.False(true, "Input tag already exists.");
                        }
                        catch (PIPointInvalidException)
                        {
                            // If it does not exist, create it
                            thisResolvedContext.InputTag = myServer.CreatePIPoint(context.InputTagName);

                            // Turn off compression, set to Double
                            thisResolvedContext.InputTag.SetAttribute(PICommonPointAttributes.Compressing, 0);
                            thisResolvedContext.InputTag.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float64);
                            AFErrors<string> errors = thisResolvedContext.InputTag.SaveAttributes(PICommonPointAttributes.Compressing,
                                                                                                  PICommonPointAttributes.PointType);

                            // If there were any errors, output them to the console then fail the test
                            if (errors != null && errors.HasErrors)
                            {
                                Console.WriteLine("Errors calling PIPoint.SaveAttributes:");
                                foreach (var item in errors.Errors)
                                {
                                    Console.WriteLine($"  {item.Key}: {item.Value}");
                                }
                            }

                            Assert.Null(errors);
                        }

                        try
                        {
                            // Try to resolve the output PIPoint object from its name, ensuring it does not already exist
                            thisResolvedContext.OutputTag = PIPoint.FindPIPoint(myServer, context.OutputTagName);
                            Assert.True(false, "Output tag already exists.");
                        }
                        catch (PIPointInvalidException)
                        {
                            // If it does not exist, let the sample create it. Store the name for easy resolution later
                            thisResolvedContext.OutputTagName = context.OutputTagName;
                        }

                        // If successful, add to the list of resolved contexts and the snapshot update subscription list
                        contextListResolved.Add(thisResolvedContext);
                    }
                    catch (Exception ex)
                    {
                        // If not successful, fail the test
                        Assert.True(false, ex.Message);
                    }
                }

                #endregion // step2

                #region step3
                Console.WriteLine("TEST: Writing values to input tags...");

                for (int i = 0; i < numValsToWrite; ++i)
                {
                    DateTime currentTime = DateTime.Now;

                    foreach (var context in contextListResolved)
                    {
                        context.InputTag.UpdateValue(new AFValue(valToWrite, currentTime), AFUpdateOption.Insert);
                    }

                    // Pause for a second to separate the values
                    Thread.Sleep(1000);
                }

                #endregion // step3

                #region step4
                Console.WriteLine("TEST: Calling main sample...");

                var source = new CancellationTokenSource();
                var token = source.Token;

                // Start the calculation
                var sampleStart = DateTime.Now;
                var success = Program.MainLoop(token);

                // Calculate the times the sample should be writing to
                int msUntilOffset = 0;
                if (settings.DefineOffsetSeconds)
                {
                    // Sample should be pausing until the next offset instance, then triggering from there
                    var secondsUntilOffset = (60 + (settings.OffsetSeconds - sampleStart.Second)) % 60;
                    msUntilOffset = (secondsUntilOffset * 1000) - sampleStart.Millisecond;
                    
                    // Reset the sample start time to when the sample will start calculating
                    sampleStart += new TimeSpan(msUntilOffset * TimeSpan.TicksPerMillisecond);
                }

                // Pause to give the calculations enough time to complete

                // allow it to wait until it starts calculating
                Thread.Sleep(msUntilOffset);

                // allow it to trigger some calculations, subtract 1 for the trigger at the start time itself
                Thread.Sleep((numValsToWrite - 1) * settings.TimerIntervalMS);

                // Cancel the operation  and wait for the sample to clean up
                source.Cancel();
                var outcome = success.Result;

                // Dispose of the cancellation token source
                if (source != null)
                {
                    Console.WriteLine("Disposing cancellation token source...");
                    source.Dispose();
                }

                // Confirm that the sample ran cleanly
                Assert.True(outcome);
                #endregion // step4

                #region step5
                Console.WriteLine("TEST: Checking the output tag values...");

                foreach (var context in contextListResolved)
                {
                    // First, resolve the output tag to ensure the sample created it successfully
                    context.OutputTag = PIPoint.FindPIPoint(myServer, context.OutputTagName);

                    // Obtain the values that should exist, plus 2. The first is 'Pt Created' and the second would represent too many values created
                    var afvals = context.OutputTag.RecordedValuesByCount(DateTime.Now, numValsToWrite + cancellationThreshold + 2, false, AFBoundaryType.Inside, null, false);

                    // Remove the initial 'Pt Created' value from the list
                    afvals.RemoveAll(afval => !afval.IsGood);

                    // Confirm the correct number of values were written, within reason
                    Assert.True((afvals.Count - numValsToWrite) < cancellationThreshold, $"The test wrote {afvals.Count} values but expected to write {numValsToWrite}, which exceeds the threshold of {cancellationThreshold}");
                    Assert.True(afvals.Count >= numValsToWrite, $"The test wrote {afvals.Count} values but should have written at least {numValsToWrite}");

                    // Check each value
                    for (int i = 0; i < afvals.Count; ++i)
                    {
                        // Check that the value is correct
                        Assert.Equal(valToWrite, afvals[i].ValueAsDouble());

                        // Check that the timestamp is correct, within reason
                        var timeError = new TimeSpan(0);

                        // The offset counts backwards because the AF SDK call returns a reverse time order
                        var expectedOffset = new TimeSpan((afvals.Count - i - 1) * settings.TimerIntervalMS * TimeSpan.TicksPerMillisecond);
                        var expectedTimeStamp = sampleStart + expectedOffset;

                        if (expectedTimeStamp > afvals[i].Timestamp.LocalTime)
                            timeError = expectedTimeStamp - afvals[i].Timestamp.LocalTime;
                        else
                            timeError = afvals[i].Timestamp.LocalTime - expectedTimeStamp;

                        Assert.True(timeError < errorThreshold, $"Output timestamp was of {afvals[i].Timestamp.LocalTime} was further from " +
                            $"expected value of {expectedTimeStamp} by more than acceptable error of {errorThreshold}");
                    }
                }
                #endregion // step5

                #region step6
                Console.WriteLine("TEST: Cleaning up...");

                foreach (var context in contextListResolved)
                {
                    myServer.DeletePIPoint(context.InputTag.Name);
                    myServer.DeletePIPoint(context.OutputTag.Name);
                }
                #endregion // step6
            }
            catch (Exception ex)
            {
                // If there was an exception along the way, fail the test
                Assert.True(false, ex.Message);
            }
        }
    }
}
