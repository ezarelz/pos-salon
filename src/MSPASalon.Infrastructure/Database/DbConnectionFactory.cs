using System.Globalization;
using Microsoft.Data.Sqlite;

namespace MSPASalon.Infrastructure.Database;

/// <summary>
/// Opens SQLite connections with the mandatory per-connection settings:
/// foreign keys ON (the legacy app forced them OFF) and a busy timeout.
/// WAL journal mode is persistent and is set once by <see cref="DatabaseInitializer"/>.
/// </summary>
public sealed class DbConnectionFactory
{
    static DbConnectionFactory()
    {
        // snake_case columns -> PascalCase properties for every Dapper query.
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public string DatabasePath { get; }
    private readonly string _connectionString;

    public DbConnectionFactory(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,           // single PC; avoids file locks lingering after backup/restore
            DefaultTimeout = 30,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        cmd.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Opens a write transaction immediately (BEGIN IMMEDIATE), so a business operation either gets the
    /// write lock up-front or fails before doing any work. Use for every multi-table operation.
    /// </summary>
    public static SqliteTransaction BeginWrite(SqliteConnection connection) => connection.BeginTransaction(deferred: false);

    /// <summary>
    /// A backup copied from the live (WAL) database inherits WAL mode, which creates -wal/-shm side files whenever
    /// it is opened. Switching it to a rollback journal makes the backup ONE self-contained file that can be copied safely.
    /// </summary>
    public static void MakeSelfContained(SqliteConnection backupConnection)
    {
        using var cmd = backupConnection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = DELETE;";
        cmd.ExecuteNonQuery();
    }
}

/// <summary>Date/time storage format: TEXT 'yyyy-MM-dd HH:mm:ss' (local time, sortable).</summary>
public static class DbTime
{
    public const string Format = "yyyy-MM-dd HH:mm:ss";

    public static string ToDb(DateTime value) => value.ToString(Format, CultureInfo.InvariantCulture);

    public static DateTime FromDb(string value) => DateTime.ParseExact(value, Format, CultureInfo.InvariantCulture);
}
