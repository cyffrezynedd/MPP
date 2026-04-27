using System;
using System.Linq;
using Lab.Shared;
using TestRunner;
using TestRunner.Threading;

namespace lab4
{
    internal static class Program
    {
        private static void Main()
        {
            var dll = TestPaths.ProjectTestsDll();
            var engine = new TestRunnerEngine();

            TestRunPrinter.Section("lab4");
            TestRunPrinter.WriteLine();

            static string SpawnRu(string r) => r switch
            {
                "initial-min" => "дожали до MinThreads",
                "queue-depth" => "очередь длинная, масштабирование",
                "queue-wait" => "долго ждут в очереди",
                _ => r
            };

            static string StopRu(string r) => r switch
            {
                "idle-shutdown" => "простой, IdleTimeout",
                "exit" => "выход из цикла",
                _ => r
            };

            TestRunPrinter.Section("Пул: события при нескольких задачах");
            using (var pool = new DynamicThreadPool(new DynamicThreadPoolOptions
                   {
                       MinThreads = 2,
                       MaxThreads = 4,
                       ScaleQueueThreshold = 2,
                       WorkerIdleShutdown = TimeSpan.FromSeconds(2)
                   }))
            {
                pool.ThreadStarted += (_, e) =>
                    TestRunPrinter.Pool(
                        $"Поток {e.ManagedThreadId} запущен, воркеров сейчас {e.LiveWorkerCount}. {SpawnRu(e.Reason)}");
                pool.ThreadStopped += (_, e) =>
                    TestRunPrinter.Pool(
                        $"Поток {e.ManagedThreadId} остановлен, воркеров осталось {e.LiveWorkerCount}. {StopRu(e.Reason)}");
                pool.QueueChanged += (_, e) =>
                    TestRunPrinter.Pool(
                        $"Очередь: {e.QueueLength} задач ждут, в пуле потоков {e.LiveWorkers}");
                pool.WorkItemStarted += (_, e) =>
                    TestRunPrinter.Pool(
                        $"Задача пошла на поток {e.ManagedThreadId}, в очереди пролежала ~{e.WaitedInQueue.TotalMilliseconds:F1} мс");
                pool.WorkItemCompleted += (_, e) =>
                    TestRunPrinter.Pool($"Задача на потоке {e.ManagedThreadId} закончилась");

                pool.Start();
                for (var i = 0; i < 6; i++)
                    pool.Enqueue(() => System.Threading.Thread.Sleep(20));
                pool.WaitUntilIdle(TimeSpan.FromSeconds(10));
            }

            TestRunPrinter.Section("Фильтр: Wallet и Priority >= 5");
            var filtered = engine.RunAllTests(dll, new TestRunConfiguration
            {
                MaxDegreeOfParallelism = 1,
                TestFilter = d => d.Category == "Wallet" && d.Priority >= 5
            });
            TestRunPrinter.WriteLine(
                $"Условие: категория Wallet и приоритет не ниже 5. Попало тестов: {filtered.Count}.");
            TestRunPrinter.PrintResults(filtered);

            TestRunPrinter.Section("Parameterized и Expression, все подходящие");
            var block = engine.RunAllTests(dll, new TestRunConfiguration
            {
                MaxDegreeOfParallelism = 1,
                TestFilter = d => d.Category is "Parameterized" or "Expression"
            });
            TestRunPrinter.PrintResults(block);

            TestRunPrinter.Section("Assert.That, искусственный провал");
            DemoThat();

            TestRunPrinter.WriteLine();
            TestRunPrinter.WriteLine("enter...");
            Console.ReadLine();
        }

        private static void DemoThat()
        {
            try
            {
                var expected = 100m;
                var actual = 77m;
                Assert.That(() => actual == expected, "касса");
            }
            catch (AssertionFailedException ex)
            {
                TestRunPrinter.PrintTestLine(new TestResult
                {
                    Status = TestStatus.Failed,
                    ClassName = nameof(Program),
                    MethodName = nameof(DemoThat),
                    DisplayName = "Assert.That sample",
                    Duration = TimeSpan.Zero,
                    Message = ex.Message
                });
            }
        }
    }
}
