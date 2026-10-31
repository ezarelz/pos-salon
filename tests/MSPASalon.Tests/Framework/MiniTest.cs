using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace MSPASalon.Tests.Framework;

/// <summary>Marks a test method. <see cref="Id"/> links the test to docs/TEST_CASES.md.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute(string id, string description) : Attribute
{
    public string Id { get; } = id;
    public string Description { get; } = description;
}

public sealed class AssertionException(string message) : Exception(message);

public static class Assert
{
    public static void True(bool condition, string message = "Expected true")
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void False(bool condition, string message = "Expected false") => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? what = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{what ?? "Value"}: expected <{expected}> but was <{actual}>");
    }

    public static void NotNull(object? value, string what = "Value")
    {
        if (value is null) throw new AssertionException($"{what} should not be null");
    }

    public static void Null(object? value, string what = "Value")
    {
        if (value is not null) throw new AssertionException($"{what} should be null but was <{value}>");
    }

    public static void Contains(string expectedSubstring, string? actual)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
            throw new AssertionException($"Expected text containing <{expectedSubstring}> but was <{actual}>");
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Expected {typeof(TException).Name} but got {ex.GetType().Name}: {ex.Message}");
        }
        throw new AssertionException($"Expected {typeof(TException).Name} but no exception was thrown");
    }
}

/// <summary>Discovers [Test] methods in this assembly, runs them, prints results and writes a Markdown report.</summary>
public static class TestRunner
{
    private sealed record Result(string Id, string Name, string Description, bool Passed, string? Error, long Ms);

    public static int Run(string[] args)
    {
        var filter = args.FirstOrDefault();
        var tests = Assembly.GetExecutingAssembly().GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
                .Select(m => (Type: t, Method: m, Attr: m.GetCustomAttribute<TestAttribute>())))
            .Where(x => x.Attr is not null)
            .Where(x => filter is null || x.Attr!.Id.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Attr!.Id, StringComparer.Ordinal)
            .ToList();

        var results = new List<Result>();
        foreach (var (type, method, attr) in tests)
        {
            var sw = Stopwatch.StartNew();
            string? error = null;
            object? instance = null;
            try
            {
                instance = method.IsStatic ? null : Activator.CreateInstance(type);
                method.Invoke(instance, null);
            }
            catch (TargetInvocationException tie)
            {
                var ex = tie.InnerException ?? tie;
                error = ex is AssertionException ? ex.Message : $"{ex.GetType().Name}: {ex.Message} @ {FirstFrame(ex)}";
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
            }
            finally
            {
                (instance as IDisposable)?.Dispose();
            }
            sw.Stop();
            results.Add(new Result(attr!.Id, $"{type.Name}.{method.Name}", attr.Description, error is null, error, sw.ElapsedMilliseconds));
            Console.ForegroundColor = error is null ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(error is null ? "  PASS " : "  FAIL ");
            Console.ResetColor();
            Console.WriteLine($"{attr.Id,-8} {attr.Description}");
            if (error is not null)
            {
                Console.WriteLine($"         → {error}");
                // On GitHub Actions, failures become annotations (visible on the PR and readable through the API).
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                    Console.WriteLine($"::error title=Test {attr.Id}::{Escape($"{attr.Description} → {error}")}");
            }
        }

        var passed = results.Count(r => r.Passed);
        Console.WriteLine();
        Console.WriteLine($"Total: {results.Count}   Passed: {passed}   Failed: {results.Count - passed}");
        WriteReport(results);
        return passed == results.Count && results.Count > 0 ? 0 : 1;
    }

    private static string FirstFrame(Exception ex) =>
        ex.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "-";

    /// <summary>GitHub workflow-command escaping for annotation messages.</summary>
    private static string Escape(string s) => s.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");

    private static void WriteReport(List<Result> results)
    {
        var dir = Path.Combine(FindRepoRoot(), "tests", "TestResults");
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.AppendLine("# Test Results").AppendLine();
        sb.AppendLine($"Run at: {DateTime.Now:yyyy-MM-dd HH:mm:ss} · OS: {Environment.OSVersion} · .NET {Environment.Version}").AppendLine();
        sb.AppendLine($"**{results.Count(r => r.Passed)} / {results.Count} passed**").AppendLine();
        sb.AppendLine("| ID | Test | Result | ms |").AppendLine("|---|---|---|---|");
        foreach (var r in results)
            sb.AppendLine($"| {r.Id} | {r.Description} | {(r.Passed ? "PASS" : "FAIL: " + r.Error?.Replace("|", "\\|"))} | {r.Ms} |");
        File.WriteAllText(Path.Combine(dir, "test-results.md"), sb.ToString());
        Console.WriteLine($"Report: {Path.Combine(dir, "test-results.md")}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MSPASalon.sln"))) dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
