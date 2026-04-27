using System;
using System.Reflection;

namespace TestingFramework
{
    public sealed class TestDescriptor
    {
        public required Type TestClass { get; init; }
        public required MethodInfo Method { get; init; }
        public TestClassAttribute? ClassAttr { get; init; }
        public TestMethodAttribute? MethodAttr { get; init; }
        public object?[]? CaseArguments { get; init; }
        public required string DisplayName { get; init; }
        public int? TimeoutMs { get; init; }

        public string Category => MethodAttr?.Category ?? ClassAttr?.Category ?? string.Empty;

        public int Priority => MethodAttr?.Priority ?? ClassAttr?.Priority ?? 0;

        public string Author => MethodAttr?.Author ?? string.Empty;
    }
}
