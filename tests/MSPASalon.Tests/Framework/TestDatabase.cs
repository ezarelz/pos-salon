using MSPASalon.Core.Interfaces;
using MSPASalon.Infrastructure;

namespace MSPASalon.Tests.Framework;

public sealed class FixedClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;
}

/// <summary>A fresh, migrated SQLite database in a temp folder; deleted on dispose.</summary>
public sealed class TestDatabase : IDisposable
{
    public string Directory { get; }
    public string Path { get; }
    public FixedClock Clock { get; } = new(new DateTime(2026, 10, 31, 9, 30, 0));
    public AppServices App { get; }

    public TestDatabase()
    {
        Directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mspasalon-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        Path = System.IO.Path.Combine(Directory, "test.db");
        App = new AppServices(Path, Clock);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
