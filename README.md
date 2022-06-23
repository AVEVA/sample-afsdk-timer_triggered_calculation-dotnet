# AF SDK Custom Calculation - Timer Triggered

**Version:** 1.1.4

[![Build Status](https://dev.azure.com/osieng/engineering/_apis/build/status/product-readiness/PI-System/aveva.sample-afsdk-timer_triggered_calculation-dotnet?branchName=main)](https://dev.azure.com/osieng/engineering/_build/latest?definitionId=3927&branchName=main)

Built with:
- .NET Framework 4.8
- Visual Studio 2019 - 16.0.30907.101
- AF SDK 2.10.9


The sample code in this topic demonstrates how to utilize the AF SDK to perform custom calculations that are beyond the functionality provided by Asset Analytics.

Previously, this use case was handled by either PI Advanced Computing Engine (ACE) or custom coded solutions. Following the [announcement of PI ACE's retirement](https://pisquare.osisoft.com/s/article/000036664), this sample is intended to provide assistance to customers migrating from PI ACE to a custom solution.

## Calculation Work Flow

The sample code is intended to provide a starting point that matches the feel of a periodically scheduled (timer-triggered) ACE calculation. To demonstrate scalability by reusing the same calculation logic for a large number of calculations, any number AF Elements can be specified in [appsettings.json](TimerTriggeredCalc/appsettings.placeholder.json). The calculation will build a client-side [AF data cache](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Data_AFDataCache.htm) of the calculation's input attributes for each element; the data cache stays current by subscribing to updates from the AF Server via the cache's internal [AFDataPipe object](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Data_AFDataPipe.htm). The output tag is written to via the client-side cache as well so that when its previous value is read in, it's guaranteed to be the previous value as its read is not in a race condition with the internal workings of the Data Archive to store the previous value in the snapshot or the archive file.

A [Cancellation Token](https://docs.microsoft.com/en-us/dotnet/api/system.threading.cancellationtoken?view=netframework-4.8) is used to end the calculation loop so that it can run indefinitely but be cancelled on demand. In this sample, the cancellation occurs when the user hits `ENTER`, but this is a demonstration of how to cancel the operation gracefully.

Overall Flow:  
1. A connection is made to the named AF Server, resolving the named AF Database
    1. If blank, the application will use the user's default AF Server and the AF Server's default database
    1. The connection is made implicitly under the identity of the Windows account executing the code
1. For each element in the context list:
    1. The element name is resolved into an [AFElement](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFElement.htm) object
    1. The `Temperature`, `Pressure`, `Volume`, and `Moles` attributes are resolved
    1. If successful, the attributes are added to the data cache's attribute list and the element to the contextList
    1. If the element or any attribute could not be resolved, a warning is outputted to the console and the application moves on to the next context. This allows the application to work against all resolvable contexts.
1. The AFDataCache object is configured
    1. The list of all input attributes is added to the cache
1. A [System.Timers.Timer](https://docs.microsoft.com/en-us/dotnet/api/system.timers.timer?view=netframework-4.8) object is configured to trigger the calculation on the specified interval of `TimerIntervalMS`
    1. For each calculation trigger, the application loops through the list of resolved contexts, executing the calculation against each input and writing to its corresponding output.
1. Optionally, the application pauses until the next occurrence of a specific offset from the top of the minute
    1. For example, if `OffsetSeconds` is 0, the application will pause until the top of the next minute so that the first calculation will have a :00 timestamp.
1. Since the timer waits for an entire cycle before triggering for the first time, the first calculation is explicitly called once the timer object is set up
1. The application continually triggers the calculation every interval, until the token is canceled.


Calculation Logic:  
1. A [RecordedValuesByCount](https://docs.osisoft.com/bundle/af-sdk/page/html/M_OSIsoft_AF_Data_AFData_RecordedValuesByCount.htm) call returns the 100 most recent values each for both `Temperature` and `Pressure`, backwards in time from the triggering timestamp.
1. Bad values are removed from the [AFValues](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFValues.htm) object
1. Looping indefinitely for each attribute:
    1. The standard deviation of the list of remaining values is determined
    1. Any value outside of 1.75 standard deviations is eliminated
1. Once no new values are eliminated, the mean of the remaining values is determined.
1. `Volume` is a static attribute, so its [end of stream](https://docs.osisoft.com/bundle/af-sdk/page/html/M_OSIsoft_AF_Data_AFData_EndOfStream.htm) value is returned
1. Each of these values are used, with the [Gas Constant](https://en.wikipedia.org/wiki/Gas_constant) to determine the number of moles in the reactor
    1. [AF Units of Measure](https://docs.osisoft.com/bundle/af-sdk/page/html/N_OSIsoft_AF_UnitsOfMeasure.htm) are used to ensure the values are the expected units, regardless of how they are stored in the underlying PI Points.
    1. This value is written to the output attribute's data cache, which makes it immediately available for robust reads and also starts the process of writing the value to the Data Archive
1. The previous value for the output attribute is read in, and a rate of change is determined.
    1. This value is written to the `MolarFlowRate` attribute. This is not cached as this attribute is not an input for the calculation, and therefore the client-side caching is not necessary.

## Getting Started

### Prerequisites

- The AF SDK and corresponding minimum version of .NET Framework must be installed on any machine executing this code  
Note: This sample uses `AF SDK 2.10.9` and `.NET Framework 4.8`. Older versions of the AF SDK may require code changes.
- AF Server and PI Data Archive that are accessable from the machine executing this code
- The account executing the code must have a mapping on the Data Archive for an identity with permissions to:
    - Read data from the input attribute(s) underlying PI Points
    - Write data to the output tag(s) underlying PI Points
- Optionally: If the application is creating elements and attributes, the account requires an AF Server mapping to an identity with permissions to perform these actions.

### Sample Modification

The sample is configured using the file [appsettings.placeholder.json](TimerTriggeredCalc/appsettings.placeholder.json). Before editing, rename this file to `appsettings.json`. This repository's `.gitignore` rules should prevent the file from ever being checked in to any fork or branch.

```json
{
  "AFServerName": "",             // Leave blank to use the machine's default AF Server
  "AFDatabaseName": "",           // Leave blank to use the AF Server's default database
  "TimerIntervalMS": 60000,       // How often to trigger the calculation, in ms
  "CacheTimeSpanSeconds": 86400,  // How old the data can be in the AF Data Cache
  "DefineOffsetSeconds": true,    // Whether or not to execute the first calculation at a defined offset
  "OffsetSeconds": 0,             // The offset to wait for, in sec.
  "Contexts": [                   // Array of element paths to run the calculation against
    "Reactor_1001",
    "Reactor_1002"
  ],
  "Username": "TEST_ONLY",        // Username to connect to the AF Server with for testing purposes only
  "Password": "TEST_ONLY"         // Password to connect to the AF Server with for testing purposes only
}
```

The sample uses an implicit connection to the PI Data Archive under the context of the account running the application. For guidance on using an explicit connection, see the [Connecting to a PI Data Archive AF SDK reference](https://docs.osisoft.com/bundle/af-sdk/page/html/connecting-to-a-pi-data-archive.htm).

1. Clone the sample repository to a local folder
1. In the TimerTriggeredCalc directory, create appsettings.json from [appsettings.placeholder.json](TimerTriggeredCalc/appsettings.placeholder.json) and edit the configuration as necessary
1. Edit [Program.cs](TimerTriggeredCalc/Program.cs) in the following places to customize the sample to your implementation:
    1. Change the logic in `PerformCalculation()`
    1. Change the list of attribute names in `DetermineListOfIdealGasLawCalculationAttributes()`
1. Ensure the listed elements exist on the specified AF Server and AF Database and have the necessary input and output attributes used in the sample's calculation
    1. *Note*: If using the sample's calculation as is, the [UnitTests.cs](TimerTriggeredCalcTests/UnitTests.cs) file begins with the creation of Elements, Attributes, and PI Points as necessary to run the sample and can be used as a reference for getting started.
1. Build and run the solution using Visual Studio or using cmd
    ```shell
    nuget restore
    msbuild EventTriggeredCalc.sln
    bin\debug\EventTriggeredCalc.exe
    ```
    - Note: The above command uses the [nuget CLI](https://docs.microsoft.com/en-us/nuget/consume-packages/install-use-packages-nuget-cli)
1. Observe in a client tool (PI System Explorer, PI Vision, etc) that the output attribute(s) is written to in conjunction with each new snapshot event of the input attribute(s)

## Running the Automated Test

The test in [TimerTriggeredCalcTests](TimerTriggeredCalcTests/UnitTests.cs) tests the sample's purpose of providing a framework for calculations to loop indefinitely, trigger on elapsed timer intervals by doing the following:
1. Connects to the specified AF Server
1. Creates an [AFElementTemplate](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFElementTemplate.htm) with the necessary [AFAttributeTemplates](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFAttributeTemplate.htm)
1. Creates an element with this template for each name in the conext list
1. Creates the attributes' underlying PI Points with [AFDataReference.CreateConfig](https://docs.osisoft.com/bundle/af-sdk/page/html/M_OSIsoft_AF_Asset_AFDataReference_CreateConfig_1.htm)
1. Writes to the input attributes to prepare them for the calculation
1. Creates a cancellation token source and calls the sample, passing it the token
1. Pauses to allow the sample to trigger a few cycles and write to the output tags. Then cancels the token to end the sample.
1. Checks the values of each output tag
    1. Confirms the correct number of values were written
    1. Confirms the values are correct
    1. Confirms the timestamps of the output tag values start at the defined offset and proceed at the correct interval from there
1. Clean up the test by deleting the PI Points, Elements, and Element Template

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
For the main AVEVA Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples)
