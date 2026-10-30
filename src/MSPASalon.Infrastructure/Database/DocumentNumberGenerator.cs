using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;

namespace MSPASalon.Infrastructure.Database;

/// <summary>
/// Sequential document numbers (YYMM/CODE/0001, monthly reset).
///
/// Fixes legacy defects D8/D13: the counter is incremented with one atomic UPSERT ... RETURNING
/// executed on the CALLER's transaction. If the document save fails and the transaction rolls back,
/// the increment rolls back too, so no number is consumed and no gap is created.
/// Uniqueness is additionally guaranteed by the UNIQUE constraint on each document's number column.
/// </summary>
public static class DocumentNumberGenerator
{
    public static string Next(SqliteConnection connection, SqliteTransaction transaction, string docType, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (!DocumentType.All.Contains(docType))
            throw new ArgumentException($"Unknown document type '{docType}'.", nameof(docType));

        var period = DocumentType.Period(at);
        var seq = connection.ExecuteScalar<long>("""
            INSERT INTO doc_counter(doc_type, period, last_no) VALUES (@docType, @period, 1)
            ON CONFLICT(doc_type, period) DO UPDATE SET last_no = last_no + 1
            RETURNING last_no
            """, new { docType, period }, transaction);

        if (seq > 9999)
            throw new BusinessException($"Nomor dokumen {docType} untuk periode {period} sudah mencapai batas 9999.");
        return DocumentType.Format(docType, at, seq);
    }
}
