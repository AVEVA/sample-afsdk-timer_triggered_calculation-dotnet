using Xunit;

namespace TimerTriggeredCalcTests
{
    public class UnitTests
    {
        [Fact]
        public void TimerTriggeredCalcTest()
        {
            Assert.True(TimerTriggeredCalc.Program.MainLoop(true));
        }
    }
}
