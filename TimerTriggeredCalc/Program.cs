using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading.Tasks;
using OSIsoft.AF.PI;

namespace TimerTriggeredCalc
{
    public static class Program
    {
        private static Timer aTimer;
        private static PIPoint output;
        private static IList<PIPoint> inputList;

        public static void Main(string[] args)
        {
            #region configuration

            var inputTagName = "cdt158";
            var outputTagName = "cdt158_output_eventbased";
            var timerMS = 5000; // how long to pause between cycles, in ms

            #endregion // configuration

            // Get default PI Data Archive
            var myServer = PIServers.GetPIServers().DefaultPIServer;

            // Get or create the output PI Point
            try
            {
                output = PIPoint.FindPIPoint(myServer, outputTagName);
            }
            catch (PIPointInvalidException)
            {
                output = myServer.CreatePIPoint(outputTagName);
                output.SetAttribute(PICommonPointAttributes.PointType, PIPointType.Float32);
            }

            // List to hold the PIPoint objects that we will use as inputs
            var nameList = new List<string>
            {
                inputTagName
            };

            inputList = PIPoint.FindPIPoints(myServer, nameList);

            // Create a timer with the specified interval
            aTimer = new Timer();
            aTimer.Interval = timerMS;

            // Hook up the Elapsed event for the timer.
            aTimer.Elapsed += PerformCalculation;

            aTimer.AutoReset = true;

            aTimer.Enabled = true;

            Console.WriteLine("Press the Enter key to exit the program at any time... ");
            Console.ReadLine();

        }

        private static void PerformCalculation(object source, ElapsedEventArgs e)
        {
            foreach (var input in inputList)
            {
                Console.WriteLine($"Calculation performed on {input.Name} at time of {e.SignalTime.ToLocalTime()}, writing to {output.Name}");
            }
        }
    }
}
