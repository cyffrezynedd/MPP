using System;

namespace TestingFramework
{
    public class TestFailedException : Exception
    {
        public TestFailedException(string message) : base(message) { }
    }

    public class AssertionFailedException : TestFailedException
    {
        public AssertionFailedException(string message) : base(message) { }
    }

    public class TestFrameworkException : Exception
    {
        public TestFrameworkException(string message) : base(message) { }
    }
}
