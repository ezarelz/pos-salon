using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Validation;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>
/// Shared Save/Delete/code-suggestion workflow for master data.
/// Save = normalize → field validation → (in one write transaction) uniqueness + DB-dependent validation
/// → insert/update → child rows → commit. Any error rolls everything back.
/// </summary>
public abstract class MasterServiceBase<T> : IMasterService<T> where T : class, IMasterEntity
{
    protected readonly DbConnectionFactory Db;
    protected readonly IClock Clock;

    protected MasterServiceBase(DbConnectionFactory db, IClock clock)
    {
        Db = db;
        Clock = clock;
    }

    /// <summary>Physical table name (constant, never user input).</summary>
    protected abstract string Table { get; }
    /// <summary>Prefix for suggested codes, e.g. "CST" → CST001.</summary>
    protected abstract string CodePrefix { get; }
    protected abstract string EntityLabel { get; }

    public abstract IReadOnlyList<T> GetAll();
    public abstract T? Get(long id);
    protected abstract List<string> ValidateFields(T entity);
    protected abstract long Insert(SqliteConnection c, SqliteTransaction tx, T entity, string now);
    protected abstract void Update(SqliteConnection c, SqliteTransaction tx, T entity, string now);

    protected virtual void ValidateWithDatabase(SqliteConnection c, SqliteTransaction tx, T entity, List<string> errors) { }
    protected virtual void AfterSave(SqliteConnection c, SqliteTransaction tx, T entity) { }
    protected virtual void BeforeDelete(SqliteConnection c, SqliteTransaction tx, long id) { }
    public virtual string? DeleteWarning(long id) => null;

    protected virtual void Normalize(T entity)
    {
        entity.Code = MasterValidators.NormalizeCode(entity.Code);
        entity.Name = (entity.Name ?? "").Trim();
    }

    public long Save(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Normalize(entity);
        var errors = ValidateFields(entity);

        using var c = Db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);

        if (entity.Id > 0 && c.ExecuteScalar<long>($"SELECT COUNT(*) FROM {Table} WHERE id = @id", new { id = entity.Id }, tx) == 0)
            throw new BusinessException($"{EntityLabel} tidak ditemukan (mungkin sudah dihapus).");

        if (errors.Count == 0 && c.ExecuteScalar<long>(
                $"SELECT COUNT(*) FROM {Table} WHERE code = @code COLLATE NOCASE AND id <> @id",
                new { code = entity.Code, id = entity.Id }, tx) > 0)
            errors.Add($"Kode '{entity.Code}' sudah dipakai {EntityLabel.ToLowerInvariant()} lain.");

        if (errors.Count == 0) ValidateWithDatabase(c, tx, entity, errors);
        if (errors.Count > 0) throw new BusinessException(errors);

        var now = DbTime.ToDb(Clock.Now);
        if (entity.Id > 0) Update(c, tx, entity, now);
        else entity.Id = Insert(c, tx, entity, now);
        AfterSave(c, tx, entity);
        tx.Commit();
        return entity.Id;
    }

    public string? Delete(long id)
    {
        var warning = DeleteWarning(id);
        using var c = Db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);
        BeforeDelete(c, tx, id);
        int affected;
        try
        {
            affected = c.Execute($"DELETE FROM {Table} WHERE id = @id", new { id }, tx);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (FK RESTRICT)
        {
            throw new BusinessException($"{EntityLabel} tidak bisa dihapus karena masih dipakai data lain.");
        }
        if (affected == 0) throw new BusinessException($"{EntityLabel} tidak ditemukan (mungkin sudah dihapus).");
        tx.Commit();
        return warning;
    }

    /// <summary>Next free code: prefix + (highest numeric suffix + 1), zero-padded to 3 digits.</summary>
    public string SuggestNextCode()
    {
        using var c = Db.Open();
        var codes = c.Query<string>($"SELECT code FROM {Table} WHERE code LIKE @p", new { p = CodePrefix + "%" });
        var max = 0;
        foreach (var code in codes)
        {
            var suffix = code[CodePrefix.Length..];
            if (suffix.Length > 0 && suffix.All(char.IsAsciiDigit) &&
                int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max)
                max = n;
        }
        return $"{CodePrefix}{max + 1:000}";
    }

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
