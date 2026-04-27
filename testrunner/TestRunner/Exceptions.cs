using System;

namespace TestRunner
{
    public class TestFailedException : Exception
    {
        public TestFailedException(string message) : base(message) { }
    }

    public class AssertionFailedException : TestFailedException
    {
        public AssertionFailedException(string message) : base(message) { }
    }

    public class TestRunnerException : Exception
    {
        public TestRunnerException(string message) : base(message) { }
    }
}
