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
using Xunit;
using Xunit.Abstractions;


namespace TimerTriggeredCalcTests
{
    public class UnitTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public UnitTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public void TimerTriggeredCalcTest()
        {

            const int NumValuesToWrite = 3; // must be 2 or more to check for proper interval spacing
            TimeSpan errorThreshold = new TimeSpan(0, 0, 0, 1); // 1s time max error is acceptable due to race condition between sample and test
            const int CancellationThreshold = 15; // canceling the timer object is slightly unpredictable, don't fail the test unless it loops far too many times

            const double TemperatureValueToWrite = 273.0;
            const double PressureValueToWrite = 2280.0;
            const int VolumeValue = 500;
            const double GasConstant = 62.363598221529; // units of  L * Torr / (K * mol)
            const double ExpectedMolesOutput = PressureValueToWrite * VolumeValue / (GasConstant * TemperatureValueToWrite);
            List<AFElement> contextElementList = new List<AFElement>();
            const string TemplateName = "EventTriggeredSampleTemplate";

            AFDatabase myAFDB = null;

            try
            {
                #region configurationSettings
                string solutionFolderName = Directory.GetParent(Directory.GetCurrentDirectory())?.Parent?.Parent?.FullName;
                AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(solutionFolderName + "/TimerTriggeredCalc/appsettings.json"));

                if (settings == null) throw new FileNotFoundException("Could not find appsettings.json file");
                #endregion // configurationSettings

                #region step1
                _testOutputHelper.WriteLine("TEST: Resolving AF Server object...");

                PISystems myPISystems = new PISystems();
                PISystem myPISystem;

                myPISystem = string.IsNullOrWhiteSpace(settings.AFServerName) ? myPISystems.DefaultPISystem : myPISystems[settings.AFServerName];
                
                if (myPISystem is null)
                {
                    _testOutputHelper.WriteLine("Create entry for AF Server...");
                    PISystem.CreatePISystem(settings.AFServerName).Dispose();
                    myPISystem = myPISystems[settings.AFServerName];
                }

                // Connect using credentials if they exist in settings
                if (!string.IsNullOrWhiteSpace(settings.Username) && !string.IsNullOrWhiteSpace(settings.Password))
                {
                    _testOutputHelper.WriteLine("Connect to AF Server using provided credentials...");
                    NetworkCredential credential = new NetworkCredential(settings.Username, settings.Password);
                    myPISystem.Connect(credential);
                }

                _testOutputHelper.WriteLine("Resolving AF Database object...");

                myAFDB = string.IsNullOrWhiteSpace(settings.AFDatabaseName) ? myPISystem.Databases.DefaultDatabase : myPISystem.Databases[settings.AFDatabaseName];
                #endregion // step 1

                #region step2
                _testOutputHelper.WriteLine("TEST: Creating element template to test against...");

                AFElementTemplate eventTriggeredTemplate = myAFDB.ElementTemplates.Add(TemplateName);

                AFAttributeTemplate tempInputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Temperature");
                tempInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["K"];
                tempInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                tempInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                AFAttributeTemplate pressInputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Pressure");
                pressInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["torr"];
                pressInputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                pressInputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                AFAttributeTemplate volInputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Volume");
                volInputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["L"];
                volInputTemplate.SetValue(VolumeValue, myPISystem.UOMDatabase.UOMs["L"]);

                AFAttributeTemplate molOutputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("Moles");
                molOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol"];
                molOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                AFAttributeTemplate molRateOutputTemplate = eventTriggeredTemplate.AttributeTemplates.Add("MolarFlowRate");
                molRateOutputTemplate.DefaultUOM = myPISystem.UOMDatabase.UOMs["mol/s"];
                molRateOutputTemplate.DataReferencePlugIn = AFDataReference.GetPIPointDataReference(myPISystem);
                molRateOutputTemplate.ConfigString = @"\\%Server%\%Element%.%Attribute%;pointtype=Float64;compressing=0";

                _testOutputHelper.WriteLine("TEST: Creating elements to test against...");

                // create elements from context list
                foreach (string context in settings.Contexts)
                {
                    AFElement thisElement = new AFElement(context, eventTriggeredTemplate);
                    myAFDB.Elements.Add(thisElement);
                    contextElementList.Add(thisElement);
                }

                // check in
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);

                // create or update reference
                AFDataReference.CreateConfig(myAFDB, null);

                _testOutputHelper.WriteLine("TEST: Writing values to input tags...");
                foreach (AFElement context in contextElementList)
                {
                    context.Attributes["Temperature"].Data.UpdateValue(new AFValue(TemperatureValueToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["K"]), AFUpdateOption.Insert);
                    context.Attributes["Pressure"].Data.UpdateValue(new AFValue(PressureValueToWrite, DateTime.Now, myPISystem.UOMDatabase.UOMs["torr"]), AFUpdateOption.Insert);
                }
                #endregion // step2

                #region step3
                _testOutputHelper.WriteLine("TEST: Calling main sample...");

                CancellationTokenSource source = new CancellationTokenSource();
                CancellationToken token = source.Token;

                // Start the calculation
                DateTime sampleStart = DateTime.Now;
                System.Threading.Tasks.Task<bool> success = Program.MainLoop(token);
                #endregion // step3

                #region step4
                _testOutputHelper.WriteLine("TEST: Pausing until sample should be canceled...");
                // Calculate the times the sample should be writing to
                int msUntilOffset = 0;
                if (settings.DefineOffsetSeconds)
                {
                    // Sample should be pausing until the next offset instance, then triggering from there
                    int secondsUntilOffset = (60 + (settings.OffsetSeconds - sampleStart.Second)) % 60;
                    msUntilOffset = (secondsUntilOffset * 1000) - sampleStart.Millisecond;
                    
                    // Reset the sample start time to when the sample will start calculating
                    sampleStart += new TimeSpan(msUntilOffset * TimeSpan.TicksPerMillisecond);
                }

                // Pause to give the calculations enough time to complete

                // allow it to wait until it starts calculating
                Thread.Sleep(msUntilOffset);

                // allow it to trigger some calculations, subtract 1 for the trigger at the start time itself
                Thread.Sleep((NumValuesToWrite - 1) * settings.TimerIntervalMS);

                // Cancel the operation  and wait for the sample to clean up
                source.Cancel();
                bool outcome = success.Result;

                // Dispose of the cancellation token source
                _testOutputHelper.WriteLine("Disposing cancellation token source...");
                source.Dispose();

                // Confirm that the sample ran cleanly
                Assert.True(outcome);
                #endregion // step4

                #region step5
                _testOutputHelper.WriteLine("TEST: Checking the output tag values...");

                foreach (AFElement context in contextElementList)
                {
                    // Obtain the values that should exist, plus 2. The first is 'Pt Created' and the second would represent too many values created
                    AFValues afValues = context.Attributes["Moles"].Data.RecordedValuesByCount(DateTime.Now, NumValuesToWrite + CancellationThreshold + 2, false, AFBoundaryType.Inside, myPISystem.UOMDatabase.UOMs["mol"], null, false);

                    // Remove the initial 'Pt Created' value from the list
                    afValues.RemoveAll(afValue => !afValue.IsGood);

                    // Confirm the correct number of values were written, within reason
                    Assert.True((afValues.Count - NumValuesToWrite) < CancellationThreshold, $"The test wrote {afValues.Count} values but expected to write {NumValuesToWrite}, which exceeds the threshold of {CancellationThreshold}");
                    Assert.True(afValues.Count >= NumValuesToWrite, $"The test wrote {afValues.Count} values but should have written at least {NumValuesToWrite}");

                    // Check each value
                    for (int i = 0; i < afValues.Count; ++i)
                    {
                        // Check that the value is correct
                        Assert.Equal(ExpectedMolesOutput, afValues[i].ValueAsDouble());

                        // Check that the timestamp is correct, within reason
                        

                        // The offset counts backwards because the AF SDK call returns a reverse time order
                        TimeSpan expectedOffset = new TimeSpan((afValues.Count - i - 1) * settings.TimerIntervalMS * TimeSpan.TicksPerMillisecond);
                        DateTime expectedTimeStamp = sampleStart + expectedOffset;

                        TimeSpan timeError = expectedTimeStamp > afValues[i].Timestamp.LocalTime
                            ? expectedTimeStamp - afValues[i].Timestamp.LocalTime
                            : afValues[i].Timestamp.LocalTime - expectedTimeStamp;

                        Assert.True(timeError < errorThreshold, $"Output timestamp was of {afValues[i].Timestamp.LocalTime} was further from " +
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
                _testOutputHelper.WriteLine("TEST: Deleting elements and element templates...");
                _testOutputHelper.WriteLine("TEST: Cleaning up...");

                foreach (AFElement context in contextElementList)
                {
                    // Delete underlying tags
                    try
                    {
                        context.Attributes["Temperature"].PIPoint.Server.DeletePIPoint(context.Attributes["Temperature"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"Temperature PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["Pressure"].PIPoint.Server.DeletePIPoint(context.Attributes["Pressure"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"Pressure PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["Moles"].PIPoint.Server.DeletePIPoint(context.Attributes["Moles"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"Moles PI Point not deleted for {context.Name}");
                    }

                    try
                    {
                        context.Attributes["MolarFlowRate"].PIPoint.Server.DeletePIPoint(context.Attributes["MolarFlowRate"].PIPoint.Name);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"MolarFlowRate PI Point not deleted for {context.Name}");
                    }

                    // Delete element
                    try
                    {
                        myAFDB.Elements.Remove(context);
                    }
                    catch
                    {
                        _testOutputHelper.WriteLine($"{context.Name} element not deleted.");
                    }
                }

                // Delete element template
                try
                {
                    myAFDB.ElementTemplates.Remove(TemplateName);
                }
                catch
                {
                    _testOutputHelper.WriteLine($"Element template {TemplateName} not deleted");
                }

                // Check in the changes
                myAFDB.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);
                #endregion // step6
            }
        }
    }
}
