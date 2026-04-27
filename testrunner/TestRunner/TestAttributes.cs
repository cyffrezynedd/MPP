using System;

namespace TestRunner
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute
    {
        public string? Category { get; set; }

        public int Priority { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public string? Name { get; }

        public TestMethodAttribute(string? name = null)
        {
            Name = name;
        }

        public string? Description { get; set; }

        public string? Category { get; set; }

        public int Priority { get; set; }

        public string? Author { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestInitializeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestCleanupAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClassInitializeAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class ClassCleanupAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class SharedContextAttribute : Attribute
    {
        public Type ContextType { get; }

        public SharedContextAttribute(Type contextType)
        {
            ContextType = contextType;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TimeoutAttribute : Attribute
    {
        public int Milliseconds { get; }

        public TimeoutAttribute(int milliseconds)
        {
            Milliseconds = milliseconds;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class TestCaseSourceAttribute : Attribute
    {
        public string MethodName { get; }

        public TestCaseSourceAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}
