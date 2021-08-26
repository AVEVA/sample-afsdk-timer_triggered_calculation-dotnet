using System.Collections.Generic;

namespace TimerTriggeredCalc
{
    public class AppSettings
    {
        /// <summary>
        /// The list of input attributes whose updates will trigger a new calculation
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Need to read from settings file deserialization")]
        public IList<string> Inputs { get; set; }

        /// <summary>
        /// The number of seconds of time series data to keep in the cache
        /// </summary>
        public int CacheTimeSpanSeconds { get; set; }

        /// <summary>
        /// The list of elements to run the calculation against
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "Need to read from settings file deserialization")]
        public IList<string> Contexts { get; set; }

        /// <summary>
        /// The name of the AF Server to use
        /// An empty string will resolve to the Default AF Server
        /// </summary>
        public string AFServerName { get; set; }

        /// <summary>
        /// The name of the AF Database to use
        /// An empty string will resolve to the Default AF Database
        /// </summary>
        public string AFDatabaseName { get; set; }

        /// <summary>
        /// The interval that the timer triggers a new calculation, in ms
        /// </summary>
        public int TimerIntervalMS { get; set; }

        /// <summary>
        /// Whether to start the first calculation at a particular offset from the top of the minute
        /// </summary>
        public bool DefineOffsetSeconds { get; set; }

        /// <summary>
        /// The number of seconds to offset from the top of the minute for the first calculation
        /// </summary>
        public int OffsetSeconds { get; set; }
    }
}
