using System.Threading.Tasks;
using TestingFramework;

namespace project.tests
{
    /// <summary>CPU-light async delays to demonstrate parallel speed-up (Lab 2).</summary>
    [TestClass(Category = "ParallelDemo", Priority = 15)]
    public class ParallelDelayTests
    {
        [TestMethod("delay-0")]
        public async Task Delay0() => await RunDelay();

        [TestMethod("delay-1")]
        public async Task Delay1() => await RunDelay();

        [TestMethod("delay-2")]
        public async Task Delay2() => await RunDelay();

        [TestMethod("delay-3")]
        public async Task Delay3() => await RunDelay();

        [TestMethod("delay-4")]
        public async Task Delay4() => await RunDelay();

        [TestMethod("delay-5")]
        public async Task Delay5() => await RunDelay();

        [TestMethod("delay-6")]
        public async Task Delay6() => await RunDelay();

        [TestMethod("delay-7")]
        public async Task Delay7() => await RunDelay();

        private static async Task RunDelay()
        {
            await Task.Delay(120);
            Assert.IsTrue(true);
        }
    }
}
