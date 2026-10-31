namespace MSPASalon.Core.Entities;

/// <summary>How stock is treated by the transaction reset (R1).</summary>
public enum StockResetMode
{
    /// <summary>All stock becomes 0; opening stock is entered afterwards through purchases or adjustments.</summary>
    Zero,
    /// <summary>The current stock of every product becomes its opening balance (OPENING ledger rows at reset time).</summary>
    KeepAsOpening,
}

public sealed record TransactionCounts(int Registrations, int Treatments, int Sales, int Purchases, int Adjustments, int Payments, int LedgerRows)
{
    public int Documents => Registrations + Treatments + Sales + Purchases + Adjustments;
    public bool IsEmpty => Documents == 0 && Payments == 0 && LedgerRows == 0;
}

/// <summary>What the reset would delete (R3 preview) and whether the data looks operational (R4).</summary>
public sealed record ResetPreview(TransactionCounts Counts, DateTime? Oldest, DateTime? Newest, bool LooksOperational,
    int ProductsWithStock, int ProductsWithNegativeStock);

public sealed record ResetResult(TransactionCounts Deleted, string BackupPath, StockResetMode Mode, int OpeningRows, IReadOnlyList<string> Warnings);

/// <summary>Contents of a backup file, checked before a restore (§16).</summary>
public sealed record BackupInfo(string Path, int SchemaVersion, string SalonName, TransactionCounts Counts, DateTime? LastTransaction, long SizeBytes);

public sealed record RestoreResult(string RestoredFrom, string SafetyBackupPath, int SchemaVersionOfBackup, int SchemaVersionNow);

public static class AdministrationRules
{
    /// <summary>Text the admin must type to confirm the reset (R3).</summary>
    public const string ResetConfirmWord = "RESET";

    /// <summary>Transactions older than this many days suggest real operational data (R4: strong warning, not blocked).</summary>
    public const int OperationalDataDays = 14;
}
