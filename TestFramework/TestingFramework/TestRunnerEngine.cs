using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TestingFramework.Threading;

namespace TestingFramework
{
    public class TestRunnerEngine
    {
        public IReadOnlyList<TestResult> RunAllTests(string testAssemblyPath, TestRunConfiguration? configuration = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(testAssemblyPath);
            var full = Path.GetFullPath(testAssemblyPath);
            if (!File.Exists(full))
                throw new FileNotFoundException("Сборка не найдена.", full);

            return RunAllTests(Assembly.LoadFrom(full), configuration);
        }

        public IReadOnlyList<TestResult> RunAllTests(Assembly testsAssembly, TestRunConfiguration? configuration = null)
        {
            var config = configuration ?? new TestRunConfiguration();
            var descriptors = Discover(testsAssembly).ToList();
            if (config.TestFilter != null)
                descriptors = descriptors.Where(config.TestFilter).ToList();

            var results = new List<TestResult>();

            foreach (var classGroup in descriptors.GroupBy(d => d.TestClass))
            {
                var first = classGroup.First();
                var testClass = first.TestClass;

                object? sharedContext = CreateSharedContextIfNeeded(testClass);
                var shell = Activator.CreateInstance(testClass)
                            ?? throw new TestFrameworkException($"Cannot create instance of {testClass.Name}.");

                InjectSharedContext(shell, sharedContext);

                var (classInit, classCleanup, initMethods, cleanupMethods) = ResolveLifecycle(testClass);

                classInit?.Invoke(shell, GetParametersOrEmpty(classInit, sharedContext));

                var testsInClass = classGroup.ToList();
                if (config.ThreadPool != null)
                    results.AddRange(RunTestsWithCustomPool(testsInClass, sharedContext, initMethods, cleanupMethods, config));
                else if (config.ParallelizeMethodsWithinClass && config.MaxDegreeOfParallelism > 1)
                    results.AddRange(RunTestsParallelTasks(testsInClass, sharedContext, initMethods, cleanupMethods, config));
                else
                {
                    foreach (var desc in testsInClass)
                        results.Add(ExecuteSingleTest(desc, sharedContext, initMethods, cleanupMethods));
                }

                classCleanup?.Invoke(shell, GetParametersOrEmpty(classCleanup, sharedContext));
            }

            return results;
        }

        private static IEnumerable<TestDescriptor> Discover(Assembly asm)
        {
            foreach (var type in asm.GetTypes().Where(t => t.GetCustomAttribute<TestClassAttribute>() != null))
            {
                var classAttr = type.GetCustomAttribute<TestClassAttribute>()!;

                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(m => m.DeclaringType == type && m.GetCustomAttribute<TestMethodAttribute>() != null);

                foreach (var method in methods)
                {
                    var mAttr = method.GetCustomAttribute<TestMethodAttribute>()!;
                    var timeout = method.GetCustomAttribute<TimeoutAttribute>()?.Milliseconds;

                    foreach (var desc in ExpandCases(type, method, classAttr, mAttr, timeout))
                        yield return desc;
                }
            }
        }

        private static IEnumerable<TestDescriptor> ExpandCases(
            Type testClass,
            MethodInfo method,
            TestClassAttribute classAttr,
            TestMethodAttribute mAttr,
            int? timeoutMs)
        {
            var baseDisplay = mAttr.Name ?? method.Name;
            var sourceAttr = method.GetCustomAttribute<TestCaseSourceAttribute>();

            if (sourceAttr == null)
            {
                yield return new TestDescriptor
                {
                    TestClass = testClass,
                    Method = method,
                    ClassAttr = classAttr,
                    MethodAttr = mAttr,
                    CaseArguments = null,
                    DisplayName = baseDisplay,
                    TimeoutMs = timeoutMs
                };
                yield break;
            }

            var sourceMethod = testClass.GetMethod(
                sourceAttr.MethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);

            if (sourceMethod == null)
                throw new TestFrameworkException($"TestCaseSource '{sourceAttr.MethodName}' not found on {testClass.Name}.");

            var enumerable = sourceMethod.Invoke(null, null) as IEnumerable
                ?? throw new TestFrameworkException($"TestCaseSource '{sourceAttr.MethodName}' returned null.");

            var i = 0;
            foreach (var item in enumerable)
            {
                object?[] row = item switch
                {
                    object[] arr => arr,
                    null => new object?[] { null },
                    _ => new[] { item }
                };

                yield return new TestDescriptor
                {
                    TestClass = testClass,
                    Method = method,
                    ClassAttr = classAttr,
                    MethodAttr = mAttr,
                    CaseArguments = row,
                    DisplayName = $"{baseDisplay} [case {i}]",
                    TimeoutMs = timeoutMs
                };
                i++;
            }
        }

        private static (MethodInfo? classInit, MethodInfo? classCleanup, List<MethodInfo> init, List<MethodInfo> cleanup)
            ResolveLifecycle(Type testClass)
        {
            MethodInfo? classInit = null;
            MethodInfo? classCleanup = null;
            var init = new List<MethodInfo>();
            var cleanup = new List<MethodInfo>();

            foreach (var method in testClass.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.GetCustomAttribute<ClassInitializeAttribute>() != null)
                    classInit = method;
                if (method.GetCustomAttribute<ClassCleanupAttribute>() != null)
                    classCleanup = method;
                if (method.GetCustomAttribute<TestInitializeAttribute>() != null)
                    init.Add(method);
                if (method.GetCustomAttribute<TestCleanupAttribute>() != null)
                    cleanup.Add(method);
            }

            return (classInit, classCleanup, init, cleanup);
        }

        private List<TestResult> RunTestsParallelTasks(
            List<TestDescriptor> tests,
            object? sharedContext,
            List<MethodInfo> initMethods,
            List<MethodInfo> cleanupMethods,
            TestRunConfiguration config)
        {
            var sem = new SemaphoreSlim(Math.Max(1, config.MaxDegreeOfParallelism));
            var bag = new ConcurrentBag<TestResult>();

            var tasks = tests.Select(desc => Task.Run(async () =>
            {
                await sem.WaitAsync().ConfigureAwait(false);
                try
                {
                    bag.Add(ExecuteSingleTest(desc, sharedContext, initMethods, cleanupMethods));
                }
                finally
                {
                    sem.Release();
                }
            })).ToArray();

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            return bag.ToList();
        }

        private List<TestResult> RunTestsWithCustomPool(
            List<TestDescriptor> tests,
            object? sharedContext,
            List<MethodInfo> initMethods,
            List<MethodInfo> cleanupMethods,
            TestRunConfiguration config)
        {
            var pool = config.ThreadPool ?? throw new ArgumentNullException(nameof(config.ThreadPool));
            var latch = new CountdownEvent(tests.Count);
            var bag = new ConcurrentBag<TestResult>();

            foreach (var desc in tests)
            {
                var captured = desc;
                pool.Enqueue(() =>
                {
                    try
                    {
                        bag.Add(ExecuteSingleTest(captured, sharedContext, initMethods, cleanupMethods));
                    }
                    finally
                    {
                        latch.Signal();
                    }
                });
            }

            latch.Wait();
            return bag.ToList();
        }

        private TestResult ExecuteSingleTest(
            TestDescriptor desc,
            object? sharedContext,
            List<MethodInfo> initMethods,
            List<MethodInfo> cleanupMethods)
        {
            var result = new TestResult
            {
                ClassName = desc.TestClass.Name,
                MethodName = desc.Method.Name,
                DisplayName = desc.DisplayName
            };

            var sw = Stopwatch.StartNew();
            try
            {
                var instance = Activator.CreateInstance(desc.TestClass)
                               ?? throw new TestFrameworkException($"Cannot create {desc.TestClass.Name}.");

                InjectSharedContext(instance, sharedContext);

                foreach (var init in initMethods)
                    init.Invoke(instance, GetParametersOrEmpty(init, sharedContext));

                var args = BuildInvokeArguments(desc.Method, sharedContext, desc.CaseArguments);
                InvokeTestMethod(desc.Method, instance, args, desc.TimeoutMs);

                foreach (var cleanup in cleanupMethods)
                    cleanup.Invoke(instance, GetParametersOrEmpty(cleanup, sharedContext));

                result.Status = TestStatus.Passed;
                result.Message = "OK";
            }
            catch (Exception ex) when (ex is TestFailedException or AssertionFailedException)
            {
                result.Status = TestStatus.Failed;
                result.Message = ex.Message;
            }
            catch (TimeoutException tex)
            {
                result.Status = TestStatus.Failed;
                result.Message = tex.Message;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                var inner = tie.InnerException;
                if (inner is TestFailedException or AssertionFailedException)
                {
                    result.Status = TestStatus.Failed;
                    result.Message = inner.Message;
                }
                else
                {
                    result.Status = TestStatus.Error;
                    result.Message = inner.ToString();
                }
            }
            catch (Exception ex)
            {
                result.Status = TestStatus.Error;
                result.Message = ex.ToString();
            }
            finally
            {
                sw.Stop();
                result.Duration = sw.Elapsed;
            }

            return result;
        }

        private static void InvokeTestMethod(MethodInfo method, object instance, object?[] args, int? timeoutMs)
        {
            if (timeoutMs is > 0)
            {
                Exception? caught = null;
                var done = new ManualResetEventSlim(false);
                var worker = new Thread(() =>
                {
                    try
                    {
                        InvokeCore(method, instance, args);
                    }
                    catch (Exception ex)
                    {
                        caught = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                })
                {
                    IsBackground = true
                };

                worker.Start();
                if (!done.Wait(timeoutMs.Value))
                    throw new TimeoutException($"Test exceeded {timeoutMs} ms.");

                if (caught != null)
                    throw caught;
            }
            else
            {
                InvokeCore(method, instance, args);
            }
        }

        private static void InvokeCore(MethodInfo method, object instance, object?[] args)
        {
            var invokeResult = method.Invoke(instance, args);
            if (invokeResult is Task task)
                task.GetAwaiter().GetResult();
        }

        private static object?[] BuildInvokeArguments(MethodInfo method, object? sharedContext, object?[]? caseArgs)
        {
            var ps = method.GetParameters();
            var args = new object?[ps.Length];
            var idx = 0;

            if (sharedContext != null && ps.Length > 0 &&
                ps[0].ParameterType.IsAssignableFrom(sharedContext.GetType()))
            {
                args[0] = sharedContext;
                idx = 1;
            }

            if (caseArgs != null)
            {
                for (var i = 0; i < caseArgs.Length && idx < ps.Length; i++, idx++)
                    args[idx] = caseArgs[i];
            }

            if (idx != ps.Length)
            {
                throw new TestFrameworkException(
                    $"Bad parameter layout for {method.DeclaringType?.Name}.{method.Name}: " +
                    $"expected {ps.Length} parameters, resolved {idx}.");
            }

            return args!;
        }

        private static object?[] GetParametersOrEmpty(MethodInfo? method, object? sharedContext)
        {
            if (method == null)
                return Array.Empty<object>();

            return GetParameters(method, sharedContext);
        }

        private static object?[] GetParameters(MethodInfo method, object? sharedContext)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 1 && sharedContext != null &&
                parameters[0].ParameterType.IsAssignableFrom(sharedContext.GetType()))
            {
                return new[] { sharedContext };
            }

            if (parameters.Length == 0)
                return Array.Empty<object>();

            throw new TestFrameworkException(
                $"Method {method.DeclaringType?.Name}.{method.Name} has unsupported parameters for lifecycle hook.");
        }

        private static object? CreateSharedContextIfNeeded(Type testClass)
        {
            var sharedAttr = testClass.GetCustomAttribute<SharedContextAttribute>();
            return sharedAttr == null ? null : Activator.CreateInstance(sharedAttr.ContextType);
        }

        private static void InjectSharedContext(object instance, object? sharedContext)
        {
            if (sharedContext == null)
                return;

            var fields = instance.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType.IsAssignableFrom(sharedContext.GetType()));

            foreach (var field in fields)
                field.SetValue(instance, sharedContext);
        }
    }
}
