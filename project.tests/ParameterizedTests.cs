using System.Collections.Generic;
using TestRunner;

namespace project.tests
{
    [TestClass(Category = "Parameterized", Priority = 20)]
    public class ParameterizedTests
    {
        public static IEnumerable<object[]> SumRows()
        {
            yield return new object[] { 1, 2, 3 };
            yield return new object[] { -1, 1, 0 };
            yield return new object[] { 40, 2, 42 };
            yield return new object[] { int.MaxValue - 1, 1, int.MaxValue };
        }

        [TestMethod("Sum data-driven rows")]
        [TestCaseSource(nameof(SumRows))]
        public void Sum(int a, int b, int expected)
        {
            Assert.AreEqual(expected, a + b);
        }

        public static IEnumerable<object[]> StringRows()
        {
            yield return new object[] { "a", "b", "ab" };
            yield return new object[] { "", "x", "x" };
        }

        [TestMethod("String concat cases")]
        [TestCaseSource(nameof(StringRows))]
        public void Concat(string left, string right, string expected)
        {
            Assert.AreEqual(expected, left + right);
        }
    }
}
