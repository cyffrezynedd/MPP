using System;
using System.IO;
using TestingFramework.Threading;

namespace TestingFramework
{
    public sealed class TestRunConfiguration
    {
        public int MaxDegreeOfParallelism { get; init; } = 4;

        public bool ParallelizeMethodsWithinClass { get; init; }

        public DynamicThreadPool? ThreadPool { get; init; }

        public Func<TestDescriptor, bool>? TestFilter { get; init; }

        public TextWriter? SynchronizedLog { get; init; }

        public void Log(string line)
        {
            var w = SynchronizedLog;
            if (w == null) return;
            lock (w)
            {
                w.WriteLine(line);
            }
        }
    }
}
