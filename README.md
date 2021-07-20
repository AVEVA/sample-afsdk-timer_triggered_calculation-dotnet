# AF SDK Custom Calculation - Timer Triggered

**Version:** 1.0.0

[![Build Status](https://dev.azure.com/osieng/engineering/_apis/build/status/product-readiness/PI-System/osisoft.sample-afsdk-timer_triggered_calculation-dotnet?branchName=main)](https://dev.azure.com/osieng/engineering/_build/latest?definitionId=3927&branchName=main)

Built with .NET Framework 4.8 and AF SDK 2.10.9


The sample code in this topic demonstrates how to utilize the AF SDK to perform custom calculations that are beyond the functionality provided by Asset Analytics.

Previously, this use case was handled by either PI Advanced Calculation Engine (ACE) or custom coded solutions. Following the [announcement of PI ACE's retirement](https://pisquare.osisoft.com/s/article/000036664), this sample is intended to provide assistance to customers migrating from PI ACE to a custom solution.

## Calculation Work Flow

The sample code is intended to provide a starting point that matches the feel of a `periodic` ACE calculation executing every minute on the minute.

1. A connection is made to the default Data Archive
    1. Optionally this can be a named Data Archive
    1. The connection is made implicitly under the identity of the account executing the code
1. The output PI point is resolved, and is created if it does not exist
1. The input PI point is resolved
1. The code pauses until the top of the next minute so all execution times will be at :00
1. A [System.Timers.Timer](https://docs.microsoft.com/en-us/dotnet/api/system.timers.timer?view=netframework-4.8) object is configured to trigger the calculation every minute
    1. Since the timer waits for an entire cycle before triggering, the first calculation is explicitly called once the timer object is set up
1. The code pauses, allowing the timer object to continually trigger the calculation every minute, until the user hits `ENTER`

The calculation logic itself is not the main purpose of the sample, but it demonstrates a complex, conditional, looping calculation that is beyond the functionality of Asset Analytics.

1. A [RecordedValuesByCount](https://docs.osisoft.com/bundle/af-sdk/page/html/M_OSIsoft_AF_PI_PIPoint_RecordedValuesByCount.htm) call returns the 100 most recent values for the tag, backwards in time from the triggering timestamp
1. Bad values are removed from the [AFValues](https://docs.osisoft.com/bundle/af-sdk/page/html/T_OSIsoft_AF_Asset_AFValues.htm) object
1. Looping indefinitely:
    1. The standard deviation of the list of remaining values is determined
    1. Any value outside of 1.75 standard deviations is eliminated
1. Once no new values are eliminated, the average of the remaining values is written to the output tag with a timestamp of the trigger time


## Prerequisites

- The AF SDK and corresponding minimum version of .NET Framework must be installed on any machine executing this code  
Note: This sample uses `AF SDK 2.10.9` and `.NET Framework 4.8`. Older versions of the AF SDK may require code changes.
- A Data Archive that is accessable from the machine executing this code
    - The unit test, as written, requires CDT158 to exist on this Data Archive
- The account executing the code must have a mapping for an identity with permissions to:
    - Read data from the input tag(s)
    - Write data to the output tag(s)
    - Create the output tag(s) if necessary

## Getting Started

1. Clone the sample repository to a local folder
1. Open [TimerTriggeredCalc\Program.cs](TimerTriggeredCalc\Program.cs) and edit the configuration sections
    1. MainLoop() function
    1. PerformCalculation() function
1. Build and run the solution
1. Observe in a client tool (PI SMT, PI Vision, etc) that the output tag is created and being written to on the timer


---

For the main AF SDK Custom Calculations Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples-PI-System/tree/main/docs/AF-SDK-Custom-Calculations-Docs)  
For the main PI System Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples-PI-System)  
For the main OSIsoft Samples landing page [ReadMe](https://github.com/osisoft/OSI-Samples)