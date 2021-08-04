# AF SDK Custom Calculation - Timer Triggered

**Version:** 1.0.0

[![Build Status](https://dev.azure.com/osieng/engineering/_apis/build/status/product-readiness/PI-System/osisoft.sample-afsdk-timer_triggered_calculation-dotnet?branchName=main)](https://dev.azure.com/osieng/engineering/_build/latest?definitionId=3927&branchName=main)

Built with:
- .NET Framework 4.8
- Visual Studio 2019 - 16.0.30907.101
- AF SDK 2.10.9, and


The sample code in this topic demonstrates how to utilize the AF SDK to perform custom calculations that are beyond the functionality provided by Asset Analytics.

Previously, this use case was handled by either PI Advanced Computing Engine (ACE) or custom coded solutions. Following the [announcement of PI ACE's retirement](https://pisquare.osisoft.com/s/article/000036664), this sample is intended to provide assistance to customers migrating from PI ACE to a custom solution.

## Calculation Work Flow

The sample code is intended to provide a starting point that matches the feel of a `periodic` ACE calculation executing on a timer with an optional specified offset from the top of the minute. To demonstrate scalability by reusing the same calculation logic for a large number of calculations, any number of pairs of input and output tag names can be specified in [appsettings.json](EventTriggeredCalc\appsettings.placeholder.json); the calculation will execute against each input tag and write to the corresponding output tag.

A [Cancellation Token](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken?view=netframework-4.8) is used to end the calculation loop so that it can run indefinitely but be cancelled on demand. In this sample, the cancellation occurs when the user hits `ENTER`, but this is a demonstration of how to cancel the operation gracefully.

1. A connection is made to the named Data Archive
    1. If blank, it will use the default Data Archive
    1. The connection is made implicitly under the identity of the account executing the code
1. For each pair of input* and output tag names:
    1. The input tag name is resolved to a PIPoint object
    1. The output tag name is resolved to a PIPoint, or is created if it does not exist
    1. The pair of input and output PIPoint objects are added to the application's list of [resolved calcuation contexts](TimerTriggeredCalc\CalculationContextResolved.cs).
    1. If either PIPoint object could not be resolved, a warning is outputted to the console and the application moves on to the next pair. This allows the application to work against all resolvable contexts.
1. A [System.Timers.Timer](https://docs.microsoft.com/en-us/dotnet/api/system.timers.timer?view=netframework-4.8) object is configured to trigger the calculation on the specified interval of `TimerIntervalMS`
    1. For each calculation trigger, the application loops through the list of resolved calculation contexts, executing the calculation against each input and writing to its corresponding output.
1. Optionally, the application pauses until the next occurrence of a specific offset from the top of the minute
    1. For example, if `OffsetSeconds` is 0, the application will pause until the top of the next minute so that the first calculation will have a :00 timestamp.
1. Since the timer waits for an entire cycle before triggering for the first time, the first calculation is explicitly called once the timer object is set up
1. The application continually triggers the calculation every interval, until the token is canceled.

*Note: The sample uses a single input tag, but this could be expanded to multiple input tags by adding properties to the [CalculationContext.cs](TimerTriggeredCalc\CalculationContext.cs) and [CalculationContextResolved.cs](TimerTriggeredCalc\CalculationContextResolved.cs) classes, the [appsettings.json](TimerTriggeredCalc\appsettings.placeholder.json) file, and incorporating the additional input tag(s) into the [PerformCalculation](TimerTriggeredCalc\Program.cs) function logic. Since the sample uses a `context` and not simply an input tag, it should be architected in such a way to easily allow multiple input tags if desired.

The calculation logic itself is not the main purpose of the sample, but it demonstrates a complex, conditional, looping calculation that is beyond the functionality of Asset Analytics.

1. A [RecordedValuesByCount](https://docs.osisoft.com/bundle/af-sdk/page/html/M_OSIsoft_AF_PI_PIPoint_RecordedValuesByCount.htm) call returns the 100 most recent values for the tag, backwards in time from the triggering timestamp
1. Bad values are removed from the [AFValues](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFValues.htm) object
1. Looping indefinitely:
    1. The standard deviation of the list of remaining values is determined
    1. Any value outside of 1.75 standard deviations is eliminated
1. Once no new values are eliminated, the mean of the remaining values is written to the output tag with a timestamp of the trigger time


## Prerequisites

- The AF SDK and corresponding minimum version of .NET Framework must be installed on any machine executing this code  
Note: This sample uses `AF SDK 2.10.9` and `.NET Framework 4.8`. Older versions of the AF SDK may require code changes.
- A Data Archive that is accessable from the machine executing this code
- The account executing the code must have a mapping for an identity with permissions to:
    - Read data from the input tag(s)
    - Write data to the output tag(s)
    - Create the output tag(s) if necessary

## Getting Started

The sample is configured using the file [appsettings.placeholder.json](TimerTriggeredCalc\appsettings.placeholder.json). Before editing, rename this file to `appsettings.json`. This repository's `.gitignore` rules should prevent the file from ever being checked in to any fork or branch.

```json
{
  "PIDataArchive": "",            // Leave blank to use the machine's default PI Data Archive
  "TimerIntervalMS": 60000,       // How often to trigger the calculation, in ms
  "DefineOffsetSeconds": true,    // Whether or not to execute the first calculation at a defined offset
  "OffsetSeconds": 0,             // The offset to wait for, in sec.
  "CalculationContexts": [        // Array of pairs of input and output tags
    {
      "InputTagName": "input1",   // The tag whose existing data is read in for the calculation
      "OutputTagName": "output1"  // The tag that is written to for the output of the calculation
    },
    {
      "InputTagName": "input2",
      "OutputTagName": "output2"
    }
  ]
}
```

The sample uses an implicit connection to the PI Data Archive under the context of the account running the application. For guidance on using an explicit connection, see the [Connecting to a PI Data Archive AF SDK reference](https://docs.osisoft.com/bundle/af-sdk/page/html/connecting-to-a-pi-data-archive.htm).

1. Clone the sample repository to a local folder
1. In the TimerTriggeredCalc directory, create appsettings.json from [appsettings.placeholder.json](TimerTriggeredCalc\appsettings.placeholder.json) and edit the configuration as necessary
1. Build and run the solution using Visual Studio or using cmd
    ```shell
    nuget restore
    msbuild EventTriggeredCalc.sln
    bin\debug\EventTriggeredCalc.exe
    ```
    - Note: The above command uses the [nuget CLI](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-nuget-cli)
1. Observe in a client tool (PI SMT, PI Vision, etc) that the output tag is created and being written to on the timer

## Running the Automated Test

The test in [TimerTriggeredCalcTests](TimerTriggeredCalcTests\UnitTests.cs) tests the sample's purpose of providing a framework for calculations to loop indefinitely, trigger on elapsed timer intervals by doing the following:
1. Connect to the specified PI Data Archive
1. For each pair of input and output tag names:
    1. Confirm that the input tag does not already exist. The test will be writing to the input tag then deleting it, so an existing tag should not be used.
    1. Create the input tag
    1. Confirm the output tag does not exist. The test will not create it, as this action should be done by the sample itself.
    1. Add the context to the list of resolved contexts
1. Writes three values to each input tag
1. Creates a cancellation token source and calls the sample, passing it the token, and pauses to allow the sample to trigger a few cycles and write to the output tags. Then cancels the token to end the sample.
1. Checks the values of each output tag
    1. Confirms the output tag was created by the sample
    1. Confirms the correct number of values were written
    1. Confirms the values are correct
    1. Confirms the timestamps of the output tag values start at the defined offset and proceed at the correct interval from there
1. Clean up the test by deleting the input and output tags

Run the test using cmd
```shell
nuget restore
msbuild EventTriggeredCalc.sln
dotnet test
```
- Note: The above command uses the [nuget CLI](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-nuget-cli)
---

For the main AF SDK Custom Calculations Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples-PI-System/tree/main/docs/AF-SDK-Custom-Calculations-Docs)  
For the main PI System Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples-PI-System)  
For the main OSIsoft Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples)