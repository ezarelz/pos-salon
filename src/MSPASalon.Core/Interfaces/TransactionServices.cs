using MSPASalon.Core.Entities;

namespace MSPASalon.Core.Interfaces;

/// <summary>Customer registration (BUSINESS_RULES §3). Every write is one database transaction.</summary>
public interface IRegistrationService
{
    IReadOnlyList<Registration> GetList(DateTime from, DateTime to);
    Registration? Get(long id);
    PostResult Create(long customerId, IReadOnlyList<ServiceLineInput> lines, string? notes, string user);
    /// <summary>Only while status = MENUNGGU (rule R4).</summary>
    void Update(long id, long customerId, IReadOnlyList<ServiceLineInput> lines, string? notes, string user);
    /// <summary>Only while status = MENUNGGU. A registration in process is cancelled through its treatment.</summary>
    void Cancel(long id, string reason, string user);
}

/// <summary>Treatment processing and payment (BUSINESS_RULES §4, §5, §9, §11).</summary>
public interface ITreatmentService
{
    /// <summary>Registrations waiting (MENUNGGU) or in process (DIPROSES), oldest first.</summary>
    IReadOnlyList<Registration> GetQueue();
    IReadOnlyList<Treatment> GetList(DateTime from, DateTime to);
    Treatment? Get(long treatmentId);
    Treatment? GetByRegistration(long registrationId);
    /// <summary>MENUNGGU → DIPROSES; creates the treatment with the same number and copies the service lines (P1, P2).</summary>
    Treatment Start(long registrationId, string user);
    /// <summary>Replace services/capsters while DIKERJAKAN (decision K4).</summary>
    void UpdateLines(long treatmentId, IReadOnlyList<ServiceLineInput> lines, string user);
    /// <summary>Planned material consumption for the given service lines (one entry per line, duplicates allowed).</summary>
    IReadOnlyList<BackflushItem> PreviewBackflush(IReadOnlyList<long> serviceIdsOfLines);
    /// <summary>
    /// Selesai &amp; Bayar (P5): time-out, totals, commission snapshot, backflush, payment, LUNAS — one transaction.
    /// Warnings report materials whose stock became negative (A4).
    /// </summary>
    PostResult Complete(long treatmentId, decimal discount, PaymentInput payment, string user);
    /// <summary>
    /// DIKERJAKAN → BATAL; or LUNAS → BATAL when <paramref name="allowPosted"/> (permission trans.cancelposted):
    /// backflush ledger rows and the payment are removed in the same transaction (K11).
    /// </summary>
    void Cancel(long treatmentId, string reason, bool allowPosted, string user);
}

public interface ISaleService
{
    IReadOnlyList<Sale> GetList(DateTime from, DateTime to);
    Sale? Get(long id);
    /// <summary>Validates, numbers, writes lines, stock-out, payment and POSTED status in one transaction (S1–S6).</summary>
    PostResult Post(long customerId, IReadOnlyList<SaleLineInput> lines, string? notes, PaymentInput payment, string user);
    void Cancel(long id, string reason, string user);
}

public interface IPurchaseService
{
    IReadOnlyList<Purchase> GetList(DateTime from, DateTime to);
    Purchase? Get(long id);
    PostResult Post(long supplierId, string? supplierRef, IReadOnlyList<PurchaseLineInput> lines, string? notes, string user);
    /// <summary>Rejected if removing the received quantities would make stock negative (§7).</summary>
    void Cancel(long id, string reason, string user);
}

public interface IAdjustmentService
{
    IReadOnlyList<StockAdjustment> GetList(DateTime from, DateTime to);
    StockAdjustment? Get(long id);
    PostResult Post(string reason, IReadOnlyList<AdjustmentLineInput> lines, string user);
    void Cancel(long id, string reason, string user);
}

public interface IStockCardService
{
    StockCard Get(long productId, DateTime from, DateTime to);
}
