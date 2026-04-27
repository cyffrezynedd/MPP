using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Lab.Shared;
using TestRunner;
using TestRunner.Threading;

namespace lab3
{
    internal static class Program
    {
        private static void Main()
        {
            var dll = TestPaths.ProjectTestsDll();
            var engine = new TestRunnerEngine();

            TestRunPrinter.Section("lab3, свой пул потоков");
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

            var options = new DynamicThreadPoolOptions
            {
                MinThreads = 2,
                MaxThreads = 8,
                ScaleQueueThreshold = 3,
                WorkerIdleShutdown = TimeSpan.FromSeconds(3),
                EnableStuckWorkReplacement = true
            };

            using var pool = new DynamicThreadPool(options);
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
            pool.Error += (_, e) =>
                TestRunPrinter.Pool(
                    $"Ошибка в работе ({e.Context}): {e.Exception.GetType().Name}: {e.Exception.Message}");

            pool.Start();

            TestRunPrinter.Section("Пауза: пул жив, очередь пустая");
            Thread.Sleep(400);

            TestRunPrinter.Section("Пачка задач подряд");
            for (var i = 0; i < 24; i++)
            {
                var c = i;
                pool.Enqueue(() => Thread.Sleep(15 + c % 5));
            }

            Thread.Sleep(200);

            TestRunPrinter.Section("По одной задаче с большими паузами");
            for (var i = 0; i < 6; i++)
            {
                pool.Enqueue(() => Thread.Sleep(30));
                Thread.Sleep(180);
            }

            pool.WaitUntilIdle(TimeSpan.FromSeconds(30));

            TestRunPrinter.Section("50 прогонов: Parameterized и Expression через этот пул");
            var sw = Stopwatch.StartNew();
            var total = 0;
            for (var run = 0; run < 50; run++)
            {
                var batch = engine.RunAllTests(dll, new TestRunConfiguration
                {
                    ThreadPool = pool,
                    TestFilter = d => d.Category is "Parameterized" or "Expression"
                });
                total += batch.Count;
                if (run is 0 or 49)
                {
                    var bad = batch.Count(t => t.Status != TestStatus.Passed);
                    TestRunPrinter.WriteLine(
                        $"Прогон {run + 1}: результатов {batch.Count}, не Passed: {bad}");
                }
            }

            sw.Stop();
            TestRunPrinter.WriteLine(
                $"Итого за 50 прогонов суммарно строк результатов: {total}, сек: {sw.Elapsed.TotalSeconds:F2}");

            TestRunPrinter.WriteLine();
            TestRunPrinter.WriteLine("enter...");
            Console.ReadLine();
        }
    }
}
