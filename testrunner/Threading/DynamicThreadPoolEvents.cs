using System;

namespace TestRunner.Threading
{
    public sealed class ThreadPoolThreadEventArgs : EventArgs
    {
        public int ManagedThreadId { get; }
        public int LiveWorkerCount { get; }
        public string Reason { get; }

        public ThreadPoolThreadEventArgs(int managedThreadId, int liveWorkerCount, string reason)
        {
            ManagedThreadId = managedThreadId;
            LiveWorkerCount = liveWorkerCount;
            Reason = reason;
        }
    }

    public sealed class ThreadPoolQueueEventArgs : EventArgs
    {
        public int QueueLength { get; }
        public int LiveWorkers { get; }

        public ThreadPoolQueueEventArgs(int queueLength, int liveWorkers)
        {
            QueueLength = queueLength;
            LiveWorkers = liveWorkers;
        }
    }

    public sealed class ThreadPoolWorkEventArgs : EventArgs
    {
        public int ManagedThreadId { get; }
        public TimeSpan WaitedInQueue { get; }

        public ThreadPoolWorkEventArgs(int managedThreadId, TimeSpan waitedInQueue)
        {
            ManagedThreadId = managedThreadId;
            WaitedInQueue = waitedInQueue;
        }
    }

    public sealed class ThreadPoolErrorEventArgs : EventArgs
    {
        public Exception Exception { get; }
        public string Context { get; }
        public int? ManagedThreadId { get; }

        public ThreadPoolErrorEventArgs(Exception exception, string context, int? managedThreadId)
        {
            Exception = exception;
            Context = context;
            ManagedThreadId = managedThreadId;
        }
    }
}
