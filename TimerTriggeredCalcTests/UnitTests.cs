using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

            int numValsToWrite = 3; // must be 2 or more to check for proper interval spacing
            var errorThreshold = new TimeSpan(0, 0, 0, 1); // 1s time max error is acceptable due to race condition between sample and test
            var cancellationThreshold = 15; // canceling the timer object is slightly unpredictable, don't fail the test unless it loops far too many times

            var tempValToWrite = 273.0;
            var pressValToWrite = 2280.0;
            var volValue = 500;
            var gasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            var expectedMolesOutput = pressValToWrite * volValue / (gasConstant * tempValToWrite);
            var contextElementList = new List<AFElement>();
            var templateName = "EventTriggeredSampleTemplate";

            AFDatabase myAFDB = null;

            try
            {
                #region configurationSettings
                string solutionFolderName = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName;
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(solutionFolderName + "/TimerTriggeredCalc/appsettings.json"));
                #endregion // configurationSettings

                #region step1
                Console.WriteLine("TEST: Resolving AF Server object...");

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
                
                if (myPISystem is null)
                {
                    Console.WriteLine("Create entry for AF Server...");
                    PISystem.CreatePISystem(settings.AFServerName).Dispose();
                    myPISystem = myPISystems[settings.AFServerName];
                }

                // Connect using credentials if they exist in settings
                if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
                {
                    Console.WriteLine("Connect to AF Server using provided credentials...");
                    var credential = new NetworkCredential(settings.Username, settings.Password);
                    myPISystem.Connect(credential);
                }

                Console.WriteLine("Resolving AF Database object...");

                if (string.IsNullOrWhiteSpace(settings.AFDatabaseName))
                {
                    // Use the default PI Data Archive
                    myAFDB = myPISystem.Databases.DefaultDatabase;
                }
                else
                {
                    myAFDB = myPISystem.Databases[settings.AFDatabaseName];
                }
                #endregion // step 1

                #region step2
                Console.WriteLine("TEST: Creating element template to test against...");

                var eventTrigerredTemplate = myAFDB.ElementTemplates.Add(templateName);

                var tempInputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Temperature");
                tempInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["K"];
                tempInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                tempInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                var pressInputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Pressure");
                pressInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["torr"];
                pressInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                pressInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                var volInputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Volume");
                volInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["L"];
                volInputTemplate.SetValue(volValue, myPISystem.UOMDatabase.UOMs["L"]);

                var molOutputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("Moles");
                molOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol"];
                molOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                var molRateOutputTemplate = eventTrigerredTemplate.AttributeTemplates.Add("MolarFlowRate");
                molRateOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol/s"];
                molRateOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molRateOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                Console.WriteLine("TEST: Creating elements to test against...");

                // create elements from context list
                foreach (var context in settings.Contexts)
                {
                    var thisElement = new AFElement(context, eventTrigerredTemplate);
                    myAFDB.Elements.Add(thisElement);
                    contextElementList.Add(thisElement);
                }

                // check in
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);

                // create or update reference
                AFDataReference.CreateConfig(myAFDB, null);

                Console.WriteLine("TEST: Writing values to input tags...");
                foreach (var context in contextElementList)
                {
                    context.Attributes["Temperature"].Data.UpdateValue(new AFValue(tempValToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["K"]), AFUpdateOption.Insert);
                    context.Attributes["Pressure"].Data.UpdateValue(new AFValue(pressValToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["torr"]), AFUpdateOption.Insert);
                }
                #endregion // step2

                #region step3
                Console.WriteLine("TEST: Calling main sample...");

                var source = new CancellationTokenSource();
                var token = source.Token;

                // Start the calculation
                var sampleStart = DateTime.Now;
                var success = Program.MainLoop(token);
                #endregion // step3

                #region step4
                Console.WriteLine("TEST: Pausing until sample should be canceled...");
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

                foreach (var context in contextElementList)
                {
                    // Obtain the values that should exist, plus 2. The first is 'Pt Created' and the second would represent too many values created
                    var afvals = context.Attributes["Moles"].Data.RecordedValuesByCount(DateTime.Now, numValsToWrite + cancellationThreshold + 2, false, AFBoundaryType.Inside, myPISystem.UOMDatabase.UOMs["mol"], null, false);

                    // Remove the initial 'Pt Created' value from the list
                    afvals.RemoveAll(afval => !afval.IsGood);

                    // Confirm the correct number of values were written, within reason
                    Assert.True((afvals.Count - numValsToWrite) < cancellationThreshold, $"The test wrote {afvals.Count} values but expected to write {numValsToWrite}, which exceeds the threshold of {cancellationThreshold}");
                    Assert.True(afvals.Count >= numValsToWrite, $"The test wrote {afvals.Count} values but should have written at least {numValsToWrite}");

                    // Check each value
                    for (int i = 0; i < afvals.Count; ++i)
                    {
                        // Check that the value is correct
                        Assert.Equal(expectedMolesOutput, afvals[i].ValueAsDouble());

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
            }
            catch (Exception ex)
            {
                // If there was an exception along the way, fail the test
                Assert.True(false, ex.Message);
            }
            finally
            {
                #region step6
                Console.WriteLine("TEST: Deleting elements and element templates...");
                Console.WriteLine("TEST: Cleaning up...");

                foreach (var context in contextElementList)
                {
                    // Delete underlying tags
                    try
                    {
                        context.Attributes["Temperature"].PIPoint.Server.DeletePIPoint(context.Attributes["Temperature"].PIPoint.Name);
                    }
                    catch
                    {
                        Console.WriteLine($"Temperature PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["Pressure"].PIPoint.Server.DeletePIPoint(context.Attributes["Pressure"].PIPoint.Name);
                    }
                    catch
                    {
                        Console.WriteLine($"Pressure PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["Moles"].PIPoint.Server.DeletePIPoint(context.Attributes["Moles"].PIPoint.Name);
                    }
                    catch
                    {
                        Console.WriteLine($"Moles PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["MolarFlowRate"].PIPoint.Server.DeletePIPoint(context.Attributes["MolarFlowRate"].PIPoint.Name);
                    }
                    catch
                    {
                        Console.WriteLine($"MolarFlowRate PI Point not deleted for {context.Name}");
                    }

                    // Delete element
                    try
                    {
                        myAFDB.Elements.Remove(context);
                    }
                    catch
                    {
                        Console.WriteLine($"{context.Name} element not deleted.");
                    }
                }

                // Delete element template
                try
                {
                    myAFDB.ElementTemplates.Remove(templateName);
                }
                catch
                {
                    Console.WriteLine($"Element template {templateName} not deleted");
                }

                // Check in the changes
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);
                #endregion // step6
            }
        }
    }
}
