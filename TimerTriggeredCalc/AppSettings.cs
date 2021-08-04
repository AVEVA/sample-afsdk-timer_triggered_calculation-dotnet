using System.Collections.Generic;

namespace TimerTriggeredCalc
{
    public class AppSettings
    {
        /// <summary>
        /// The unresolved list of input and output tag names for the calculation to run against
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Allow in configuration, reading from file")]
        public IList<CalculationContext> CalculationContexts { get; set; }

        /// <summary>
        /// The name of the PI Data Archive to use
        /// An empty string will resolve to the Default PI Data Archive
        /// </summary>
        public string PIDataArchiveName { get; set; }

        /// <summary>
        /// The interval that the timer triggers the calculation, in ms
        /// </summary>
        public int TimerIntervalMS { get; set; }

        /// <summary>
        /// Whether or not to start the timer at a specific number of seconds (eg, the top of the minute)
        /// </summary>
        public bool DefineOffsetSeconds { get; set; }

        /// <summary>
        /// How many seconds to offset from the top of the minute
        /// </summary>
        public int OffsetSeconds { get; set; }
    }
}
