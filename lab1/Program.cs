using System.IO;
using Lab.Shared;
using TestRunner;

namespace lab1
{
    internal static class Program
    {
        private static void Main()
        {
            var log = new StringWriter();
            var engine = new TestRunnerEngine();
            var dll = TestPaths.ProjectTestsDll();

            TestRunPrinter.Section("lab1: все тесты подряд, без параллели");
            TestRunPrinter.WriteLine();

            var results = engine.RunAllTests(dll, new TestRunConfiguration
            {
                MaxDegreeOfParallelism = 1,
                ParallelizeMethodsWithinClass = false,
                SynchronizedLog = log
            });

            TestRunPrinter.PrintResults(results);
            TestRunPrinter.WriteLine();
            TestRunPrinter.WriteLine("enter...");
            System.Console.ReadLine();
        }
    }
}
