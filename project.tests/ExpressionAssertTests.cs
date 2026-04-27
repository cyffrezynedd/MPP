using TestRunner;

namespace project.tests
{
    [TestClass(Category = "Expression", Priority = 30)]
    public class ExpressionAssertTests
    {
        [TestMethod("Expression tree assert — happy path")]
        public void That_Compares_WithReadableStructure()
        {
            decimal balance = 75m;
            decimal expected = 75m;
            Assert.That(() => balance == expected);
        }

        [TestMethod("Expression tree assert — compound")]
        public void That_AndAlso()
        {
            var x = 3;
            var y = 4;
            Assert.That(() => x < y && y < 10);
        }
    }
}
