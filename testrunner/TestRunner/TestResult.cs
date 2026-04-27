using System;

namespace TestRunner
{
    public enum TestStatus
    {
        Passed,
        Failed,
        Error
    }

    public class TestResult
    {
        public string ClassName { get; set; } = "";

        public string MethodName { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public TestStatus Status { get; set; }

        public string Message { get; set; } = "";

        public TimeSpan Duration { get; set; }
    }
}
