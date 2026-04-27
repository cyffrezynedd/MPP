using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace TestRunner
{
    public static class Assert
    {
        public static void IsTrue(bool condition, string? message = null)
        {
            if (!condition)
                throw new AssertionFailedException(message ?? "Expected condition to be true.");
        }

        public static void IsFalse(bool condition, string? message = null)
        {
            if (condition)
                throw new AssertionFailedException(message ?? "Expected condition to be false.");
        }

        public static void AreEqual<T>(T expected, T actual, string? message = null)
        {
            if (!Equals(expected, actual))
                throw new AssertionFailedException(message ?? $"Expected: {expected}, Actual: {actual}");
        }

        public static void AreNotEqual<T>(T notExpected, T actual, string? message = null)
        {
            if (Equals(notExpected, actual))
                throw new AssertionFailedException(message ?? $"Did not expect: {notExpected}");
        }

        public static void IsNull(object? obj, string? message = null)
        {
            if (obj != null)
                throw new AssertionFailedException(message ?? "Expected null.");
        }

        public static void IsNotNull(object? obj, string? message = null)
        {
            if (obj == null)
                throw new AssertionFailedException(message ?? "Expected not null.");
        }

        public static void GreaterThan<T>(T actual, T min, string? message = null) where T : IComparable<T>
        {
            if (actual.CompareTo(min) <= 0)
                throw new AssertionFailedException(message ?? $"Expected {actual} > {min}");
        }

        public static void LessThan<T>(T actual, T max, string? message = null) where T : IComparable<T>
        {
            if (actual.CompareTo(max) >= 0)
                throw new AssertionFailedException(message ?? $"Expected {actual} < {max}");
        }

        public static void StringContains(string str, string substring, string? message = null)
        {
            if (str == null || substring == null || !str.Contains(substring))
                throw new AssertionFailedException(message ?? $"Expected '{str}' to contain '{substring}'.");
        }

        public static void CollectionContains(ICollection collection, object? element, string? message = null)
        {
            bool found = collection?.Cast<object>().Contains(element) ?? false;
            if (!found)
                throw new AssertionFailedException(message ?? "Expected collection to contain element.");
        }

        public static void That(Expression<Func<bool>> predicate, string? message = null)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            Func<bool> compiled = predicate.Compile();
            bool ok;
            try
            {
                ok = compiled();
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException(
                    (message != null ? message + Environment.NewLine : string.Empty) +
                    $"При вычислении предиката: {ex.GetType().Name}: {ex.Message}");
            }

            if (!ok)
            {
                var detail = ExplainExpression(predicate.Body, predicate.Parameters);
                throw new AssertionFailedException(
                    (message != null ? message + Environment.NewLine : string.Empty) +
                    "Условие не выполнено." + Environment.NewLine + detail);
            }
        }

        public static void Throws<TException>(Action action, string? message = null)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new AssertionFailedException(
                    message ?? $"Expected exception {typeof(TException).Name}, but got {ex.GetType().Name}");
            }

            throw new AssertionFailedException(
                message ?? $"Expected exception {typeof(TException).Name}, but no exception was thrown.");
        }

        private static string ExplainExpression(Expression body, IEnumerable<ParameterExpression> parameters)
        {
            try
            {
                return body switch
                {
                    BinaryExpression b => ExplainBinary(b, parameters),
                    UnaryExpression u when u.NodeType == ExpressionType.Not =>
                        $"NOT ({ExplainExpression(u.Operand, parameters)})",
                    _ => $"{body.NodeType}: {TryEvalToString(body, parameters)}"
                };
            }
            catch
            {
                return body.ToString();
            }
        }

        private static string ExplainBinary(BinaryExpression b, IEnumerable<ParameterExpression> parameters)
        {
            var op = b.NodeType switch
            {
                ExpressionType.Equal => "==",
                ExpressionType.NotEqual => "!=",
                ExpressionType.GreaterThan => ">",
                ExpressionType.GreaterThanOrEqual => ">=",
                ExpressionType.LessThan => "<",
                ExpressionType.LessThanOrEqual => "<=",
                ExpressionType.AndAlso => "&&",
                ExpressionType.OrElse => "||",
                _ => b.NodeType.ToString()
            };

            var left = TryEvalToString(b.Left, parameters);
            var right = TryEvalToString(b.Right, parameters);
            return
                $"Сравнение: слева {left} {op} справа {right} (типы: {TypeName(b.Left.Type)} и {TypeName(b.Right.Type)})";
        }

        private static string TypeName(Type t) => t.FullName ?? t.Name;

        private static string TryEvalToString(Expression expr, IEnumerable<ParameterExpression> parameters)
        {
            try
            {
                var lambda = Expression.Lambda(expr, parameters.ToArray());
                var fn = lambda.Compile();
                var result = fn.DynamicInvoke();
                return FormatValue(result);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                return $"<eval error: {tie.InnerException.Message}>";
            }
            catch (Exception ex)
            {
                return $"<eval error: {ex.Message}> | {expr}";
            }
        }

        private static string FormatValue(object? value) =>
            value switch
            {
                null => "null",
                string s => $"\"{s}\"",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "null"
            };
    }
}
