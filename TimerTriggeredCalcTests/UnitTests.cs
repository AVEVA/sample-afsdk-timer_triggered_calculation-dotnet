using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TimerTriggeredCalcTests
{
    [TestClass]
    public class UnitTests
    {
        [TestMethod]
        public void TimerTriggeredCalcTest()
        {
            Assert.IsTrue(TimerTriggeredCalc.Program.MainLoop(true));
        }
    }
}
