using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lab.Shared;
using TestingFramework;

namespace lab2
{
    internal static class Program
    {
        private static readonly object FileLock = new();

        private static void Main()
        {
            var dll = TestPaths.ProjectTestsDll();
            var engine = new TestRunnerEngine();
            var reportPath = Path.Combine(AppContext.BaseDirectory, "lab2-results.txt");
            using var report = new StreamWriter(new FileStream(
                reportPath, FileMode.Create, FileAccess.Write, FileShare.Read));

            TestRunPrinter.Section("lab2: параллель, копия вывода в txt", report, FileLock);
            TestRunPrinter.WriteLine(report, FileLock, reportPath);

            void Log(string line)
            {
                TestRunPrinter.WriteLine(line);
                TestRunPrinter.WriteLine(report, FileLock, line);
            }

            TestRunPrinter.Section("ParallelDemo, один поток (как очередь)", report, FileLock);
            var sw = Stopwatch.StartNew();
            var seq = engine.RunAllTests(dll, new TestRunConfiguration
            {
                MaxDegreeOfParallelism = 1,
                ParallelizeMethodsWithinClass = false,
                TestFilter = d => d.Category == "ParallelDemo"
            });
            sw.Stop();
            var seqMs = sw.ElapsedMilliseconds;
            TestRunPrinter.PrintResults(seq, report, FileLock);
            Log($"Время последовательного прогона: {seqMs} мс");

            TestRunPrinter.Section("ParallelDemo, параллельно (семафор по числу ядер)", report, FileLock);
            sw.Restart();
            var par = engine.RunAllTests(dll, new TestRunConfiguration
            {
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                ParallelizeMethodsWithinClass = true,
                TestFilter = d => d.Category == "ParallelDemo"
            });
            sw.Stop();
            var parMs = sw.ElapsedMilliseconds;
            TestRunPrinter.PrintResults(par, report, FileLock);
            Log($"Время параллельного прогона: {parMs} мс");
            if (parMs > 0 && seqMs > 0)
                Log($"Последовательный в {seqMs / (double)Math.Max(1, parMs):0.00} раз медленнее (если > 1, параллель выигрывает)");

            TestRunPrinter.Section("Категория Wallet", report, FileLock);
            var wallet = engine.RunAllTests(dll, new TestRunConfiguration
            {
                MaxDegreeOfParallelism = 2,
                ParallelizeMethodsWithinClass = true,
                TestFilter = d => d.Category == "Wallet"
            });
            TestRunPrinter.PrintResults(wallet, report, FileLock);

            TestRunPrinter.WriteLine();
            TestRunPrinter.WriteLine("enter...");
            Console.ReadLine();
        }
    }
}
