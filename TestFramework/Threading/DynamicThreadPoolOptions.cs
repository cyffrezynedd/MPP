using System;

namespace TestingFramework.Threading
{
    public sealed class DynamicThreadPoolOptions
    {
        public int MinThreads { get; init; } = 2;

        public int MaxThreads { get; init; } = 8;

        public int ScaleQueueThreshold { get; init; } = 4;

        public TimeSpan WorkerIdleShutdown { get; init; } = TimeSpan.FromSeconds(4);

        public bool EnableStuckWorkReplacement { get; init; } = true;

        public TimeSpan StuckDetectionInterval { get; init; } = TimeSpan.FromMilliseconds(400);

        public int StuckIterationsBeforeReplacement { get; init; } = 5;
    }
}
