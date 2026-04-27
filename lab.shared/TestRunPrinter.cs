using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TestRunner;

namespace Lab.Shared
{
    public static class TestRunPrinter
    {
        private static readonly object ConsoleLock = new();

        public static void Section(string title, TextWriter? mirrorPlainText = null, object? mirrorLock = null)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(title);
                Console.ResetColor();
            }

            MirrorPlain(mirrorPlainText, mirrorLock, string.Empty);
            MirrorPlain(mirrorPlainText, mirrorLock, title);
        }

        public static void Pool(string detail)
        {
            lock (ConsoleLock)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(detail);
                Console.ResetColor();
            }
        }

        public static void PrintTestLine(TestResult result)
        {
            lock (ConsoleLock)
            {
                WriteTestLineCore(result);
            }
        }

        public static void PrintResults(
            IReadOnlyList<TestResult> results,
            TextWriter? mirrorPlainText = null,
            object? mirrorLock = null)
        {
            foreach (var result in results)
            {
                PrintTestLine(result);
                MirrorPlain(mirrorPlainText, mirrorLock,
                    $"{result.Status,-7} {result.ClassName}.{result.MethodName} ({result.DisplayName}) [{result.Duration.TotalMilliseconds:F1} ms]");
                if (result.Status != TestStatus.Passed)
                    MirrorIndentedMessage(mirrorPlainText, mirrorLock, result.Message);
            }

            PrintSummary(results, null, mirrorPlainText, mirrorLock);
        }

        public static void PrintSummary(
            IReadOnlyList<TestResult> results,
            string? caption = null,
            TextWriter? mirrorPlainText = null,
            object? mirrorLock = null)
        {
            lock (ConsoleLock)
            {
                if (!string.IsNullOrEmpty(caption))
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine(caption);
                    Console.ResetColor();
                    MirrorPlain(mirrorPlainText, mirrorLock, caption);
                }

                int ok = results.Count(r => r.Status == TestStatus.Passed);
                int fail = results.Count(r => r.Status == TestStatus.Failed);
                int err = results.Count(r => r.Status == TestStatus.Error);

                Console.WriteLine("---");
                Console.WriteLine($"Итого: {results.Count}");
                MirrorPlain(mirrorPlainText, mirrorLock, "---");
                MirrorPlain(mirrorPlainText, mirrorLock, $"Итого: {results.Count}");
                Console.Write("  ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"Passed {ok}");
                Console.ResetColor();
                Console.Write(", ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"Failed {fail}");
                Console.ResetColor();
                Console.Write(", ");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"Error {err}");
                Console.ResetColor();
                Console.WriteLine();
                MirrorPlain(mirrorPlainText, mirrorLock, $"  Passed {ok}, Failed {fail}, Error {err}");
            }
        }

        private static void WriteIndentedMessageLines(string? message)
        {
            var lines = (message ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                Console.WriteLine("        (нет текста)");
                return;
            }

            foreach (var line in lines)
                Console.WriteLine($"        {line}");
        }

        private static void MirrorIndentedMessage(TextWriter? writer, object? sync, string? message)
        {
            if (writer == null || sync == null) return;
            var lines = (message ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            lock (sync)
            {
                if (lines.Length == 0)
                    writer.WriteLine("        (нет текста)");
                else
                    foreach (var line in lines)
                        writer.WriteLine($"        {line}");
            }
        }

        private static void MirrorPlain(TextWriter? writer, object? sync, string? line)
        {
            if (writer == null || sync == null || line == null) return;
            lock (sync)
                writer.WriteLine(line);
        }

        private static void WriteTestLineCore(TestResult result)
        {
            Console.ForegroundColor = result.Status switch
            {
                TestStatus.Passed => ConsoleColor.Green,
                TestStatus.Failed => ConsoleColor.Yellow,
                TestStatus.Error => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            Console.WriteLine(
                $"{result.Status,-7} {result.ClassName}.{result.MethodName} " +
                $"({result.DisplayName}) [{result.Duration.TotalMilliseconds:F1} ms]");

            if (result.Status != TestStatus.Passed)
            {
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                WriteIndentedMessageLines(result.Message);
                Console.ResetColor();
            }
            else
            {
                Console.ResetColor();
            }
        }

        public static void WriteLine(string? text = null)
        {
            lock (ConsoleLock)
            {
                Console.WriteLine(text ?? string.Empty);
            }
        }

        public static void WriteLine(TextWriter writer, object sync, string? text = null)
        {
            lock (sync)
            {
                writer.WriteLine(text ?? string.Empty);
            }
        }
    }
}
