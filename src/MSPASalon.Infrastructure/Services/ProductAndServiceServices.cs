using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Validation;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

public sealed class ProductService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Product>(db, clock)
{
    protected override string Table => "product";
    protected override string CodePrefix => "PRD";
    protected override string EntityLabel => "Produk";

    /// <summary>Raw row: quantities are scaled integers (x10,000) and are converted explicitly.</summary>
    private sealed class Row
    {
        public long Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public long? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string Unit { get; set; } = "";
        public long? ContentQtyX { get; set; }
        public string? ContentUnit { get; set; }
        public long CostPrice { get; set; }
        public long SellPrice { get; set; }
        public long StockMinX { get; set; }
        public long StockMaxX { get; set; }
        public bool IsSellable { get; set; }
        public bool IsConsumable { get; set; }
        public bool IsActive { get; set; }
        public long StockOnHandX { get; set; }

        public Product ToEntity() => new()
        {
            Id = Id, Code = Code, Name = Name, CategoryId = CategoryId, CategoryName = CategoryName, Unit = Unit,
            ContentQty = ContentQtyX.HasValue ? Quantity.FromStored(ContentQtyX.Value) : null, ContentUnit = ContentUnit,
            CostPrice = CostPrice, SellPrice = SellPrice,
            StockMin = Quantity.FromStored(StockMinX), StockMax = Quantity.FromStored(StockMaxX),
            IsSellable = IsSellable, IsConsumable = IsConsumable, IsActive = IsActive,
            StockOnHand = Quantity.FromStored(StockOnHandX),
        };
    }

    private const string Select = """
        SELECT p.id, p.code, p.name, p.category_id, k.name AS category_name, p.unit, p.content_qty_x, p.content_unit,
               p.cost_price, p.sell_price, p.stock_min_x, p.stock_max_x, p.is_sellable, p.is_consumable, p.is_active,
               p.stock_on_hand_x
        FROM product p LEFT JOIN category k ON k.id = p.category_id
        """;

    public override IReadOnlyList<Product> GetAll()
    {
        using var c = Db.Open();
        return c.Query<Row>(Select + " ORDER BY p.code").Select(r => r.ToEntity()).ToList();
    }

    public override Product? Get(long id)
    {
        using var c = Db.Open();
        return c.QuerySingleOrDefault<Row>(Select + " WHERE p.id = @id", new { id })?.ToEntity();
    }

    protected override void Normalize(Product e)
    {
        base.Normalize(e);
        e.Unit = (e.Unit ?? "").Trim().ToUpperInvariant();
        e.ContentUnit = Clean(e.ContentUnit);
        if (e.ContentQty is null) e.ContentUnit = null;
    }

    protected override List<string> ValidateFields(Product e) => MasterValidators.Validate(e);

    protected override void ValidateWithDatabase(SqliteConnection c, SqliteTransaction tx, Product e, List<string> errors)
    {
        if (e.CategoryId is > 0 && c.ExecuteScalar<long>("SELECT COUNT(*) FROM category WHERE id = @id", new { id = e.CategoryId }, tx) == 0)
            errors.Add("Kategori yang dipilih tidak ditemukan.");

        // A product used in a recipe must stay a consumable (backflush, K3).
        if (e.Id > 0 && !e.IsConsumable)
        {
            var used = c.Query<string>("""
                SELECT s.name FROM service_material m JOIN service s ON s.id = m.service_id WHERE m.product_id = @id ORDER BY s.name
                """, new { id = e.Id }, tx).ToList();
            if (used.Count > 0)
                errors.Add($"Produk masih dipakai di resep jasa ({string.Join(", ", used)}), jadi harus tetap ditandai 'Bahan habis pakai'.");
        }
    }

    private static object Params(Product e, string now) => new
    {
        e.Id, e.Code, e.Name, CategoryId = e.CategoryId is > 0 ? e.CategoryId : null, e.Unit,
        ContentQtyX = e.ContentQty.HasValue ? Quantity.ToStored(e.ContentQty.Value) : (long?)null, e.ContentUnit,
        CostPrice = Money.ToStored(e.CostPrice), SellPrice = Money.ToStored(e.SellPrice),
        StockMinX = Quantity.ToStored(e.StockMin), StockMaxX = Quantity.ToStored(e.StockMax),
        e.IsSellable, e.IsConsumable, e.IsActive, now,
    };

    // stock_on_hand_x is never written here: stock only changes through the stock ledger.
    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Product e, string now) =>
        c.ExecuteScalar<long>("""
            INSERT INTO product(code, name, category_id, unit, content_qty_x, content_unit, cost_price, sell_price,
                                stock_min_x, stock_max_x, is_sellable, is_consumable, is_active, created_at, updated_at)
            VALUES (@Code, @Name, @CategoryId, @Unit, @ContentQtyX, @ContentUnit, @CostPrice, @SellPrice,
                    @StockMinX, @StockMaxX, @IsSellable, @IsConsumable, @IsActive, @now, @now)
            RETURNING id
            """, Params(e, now), tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Product e, string now) =>
        c.Execute("""
            UPDATE product SET code = @Code, name = @Name, category_id = @CategoryId, unit = @Unit,
                   content_qty_x = @ContentQtyX, content_unit = @ContentUnit, cost_price = @CostPrice, sell_price = @SellPrice,
                   stock_min_x = @StockMinX, stock_max_x = @StockMaxX, is_sellable = @IsSellable,
                   is_consumable = @IsConsumable, is_active = @IsActive, updated_at = @now
            WHERE id = @Id
            """, Params(e, now), tx);

    public override string? DeleteWarning(long id)
    {
        using var c = Db.Open();
        var parts = new List<string>();
        var stock = c.ExecuteScalar<long?>("SELECT stock_on_hand_x FROM product WHERE id = @id", new { id }) ?? 0;
        if (stock != 0) parts.Add($"Stok saat ini masih {Quantity.Format(Quantity.FromStored(stock))}; kartu stok produk ini ikut terhapus.");
        var recipes = c.Query<string>("""
            SELECT s.name FROM service_material m JOIN service s ON s.id = m.service_id WHERE m.product_id = @id ORDER BY s.name
            """, new { id }).ToList();
        if (recipes.Count > 0) parts.Add($"Produk akan dihapus dari resep jasa: {string.Join(", ", recipes)}.");
        return parts.Count > 0 ? string.Join(Environment.NewLine, parts) : null;
    }
}

public sealed class ServiceService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Service>(db, clock)
{
    protected override string Table => "service";
    protected override string CodePrefix => "SVC";
    protected override string EntityLabel => "Jasa";

    private const string Select = """
        SELECT s.id, s.code, s.name, s.price, s.commission, s.is_active,
               (SELECT COUNT(*) FROM service_material m WHERE m.service_id = s.id) AS material_count
        FROM service s
        """;

    private sealed class MaterialRow
    {
        public long Id { get; set; }
        public long ServiceId { get; set; }
        public long ProductId { get; set; }
        public string ProductCode { get; set; } = "";
        public string ProductName { get; set; } = "";
        public string Unit { get; set; } = "";
        public long? ContentQtyX { get; set; }
        public string? ContentUnit { get; set; }
        public long QtyX { get; set; }
    }

    public override IReadOnlyList<Service> GetAll()
    {
        using var c = Db.Open();
        return c.Query<Service>(Select + " ORDER BY s.code").ToList();
    }

    public override Service? Get(long id)
    {
        using var c = Db.Open();
        var s = c.QuerySingleOrDefault<Service>(Select + " WHERE s.id = @id", new { id });
        if (s is null) return null;
        s.Materials = c.Query<MaterialRow>("""
                SELECT m.id, m.service_id, m.product_id, p.code AS product_code, p.name AS product_name, p.unit,
                       p.content_qty_x, p.content_unit, m.qty_x
                FROM service_material m JOIN product p ON p.id = m.product_id
                WHERE m.service_id = @id ORDER BY p.name
                """, new { id })
            .Select(r => new ServiceMaterial
            {
                Id = r.Id, ServiceId = r.ServiceId, ProductId = r.ProductId, ProductCode = r.ProductCode,
                ProductName = r.ProductName, Unit = r.Unit,
                ContentQty = r.ContentQtyX.HasValue ? Quantity.FromStored(r.ContentQtyX.Value) : null,
                ContentUnit = r.ContentUnit, Qty = Quantity.FromStored(r.QtyX),
            }).ToList();
        return s;
    }

    protected override List<string> ValidateFields(Service e) => MasterValidators.Validate(e);

    protected override void ValidateWithDatabase(SqliteConnection c, SqliteTransaction tx, Service e, List<string> errors)
    {
        foreach (var m in e.Materials.Where(m => m.ProductId > 0))
        {
            var p = c.QuerySingleOrDefault<(string Name, long IsConsumable)>(
                "SELECT name, is_consumable FROM product WHERE id = @id", new { id = m.ProductId }, tx);
            if (p.Name is null) errors.Add($"Produk resep (id {m.ProductId}) tidak ditemukan.");
            else if (p.IsConsumable != 1) errors.Add($"Produk '{p.Name}' bukan bahan habis pakai, jadi tidak bisa dipakai di resep.");
        }
    }

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Service e, string now) =>
        c.ExecuteScalar<long>("""
            INSERT INTO service(code, name, price, commission, is_active, created_at, updated_at)
            VALUES (@Code, @Name, @Price, @Commission, @IsActive, @now, @now) RETURNING id
            """, new { e.Code, e.Name, Price = Money.ToStored(e.Price), Commission = Money.ToStored(e.Commission), e.IsActive, now }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Service e, string now) =>
        c.Execute("""
            UPDATE service SET code = @Code, name = @Name, price = @Price, commission = @Commission,
                   is_active = @IsActive, updated_at = @now WHERE id = @Id
            """, new { e.Id, e.Code, e.Name, Price = Money.ToStored(e.Price), Commission = Money.ToStored(e.Commission), e.IsActive, now }, tx);

    /// <summary>The recipe is replaced as a whole, inside the same transaction as the header.</summary>
    protected override void AfterSave(SqliteConnection c, SqliteTransaction tx, Service e)
    {
        c.Execute("DELETE FROM service_material WHERE service_id = @id", new { id = e.Id }, tx);
        foreach (var m in e.Materials)
            c.Execute("INSERT INTO service_material(service_id, product_id, qty_x) VALUES (@s, @p, @q)",
                new { s = e.Id, p = m.ProductId, q = Quantity.ToStored(m.Qty) }, tx);
        e.MaterialCount = e.Materials.Count;
    }
}
