using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Security;
using MSPASalon.Core.Validation;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

/// <summary>
/// Imports MASTER DATA ONLY from the legacy MSPA Salon database (decision C11). Legacy transactions are
/// intentionally not migrated: they were test data and their stock/payment history is not reliable (D4, D9).
///
/// Mapping (legacy → new), branch ccode = 1 only:
///   Tcabang  → branch          Tcateg    → category        mproduct → product (+ OPENING stock ledger row)
///   mcustomer→ customer        msupplier → supplier        tkar     → employee (salary NOT imported)
///   Tservice → service         users     → app_user (password hashed, must change at next login)
/// The legacy file is opened READ-ONLY. Everything is written in ONE transaction: all or nothing.
/// </summary>
public sealed class LegacyImportService(DbConnectionFactory db, IClock clock) : ILegacyImportService
{
    public const string OpeningDocNo = "SALDO-AWAL";

    public bool TargetHasMasterData()
    {
        using var c = db.Open();
        return c.ExecuteScalar<long>("""
            SELECT (SELECT COUNT(*) FROM product) + (SELECT COUNT(*) FROM service) + (SELECT COUNT(*) FROM employee)
                 + (SELECT COUNT(*) FROM supplier) + (SELECT COUNT(*) FROM category)
                 + (SELECT COUNT(*) FROM customer WHERE is_walk_in = 0)
            """) > 0;
    }

    public ImportResult Import(string legacyDatabasePath)
    {
        if (!File.Exists(legacyDatabasePath)) throw new BusinessException($"File database lama tidak ditemukan: {legacyDatabasePath}");
        if (Path.GetFullPath(legacyDatabasePath) == db.DatabasePath) throw new BusinessException("File sumber sama dengan database aplikasi.");
        if (TargetHasMasterData())
            throw new BusinessException("Database aplikasi sudah berisi master data. Import hanya bisa dilakukan ke database yang masih kosong.");

        var result = new ImportResult();
        using var legacy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = legacyDatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        legacy.Open();
        RequireTables(legacy, "mproduct", "mcustomer", "tkar", "Tservice", "Tcateg", "msupplier", "users", "Tcabang");

        var now = clock.Now;
        var nowDb = DbTime.ToDb(now);
        using var c = db.Open();
        using var tx = DbConnectionFactory.BeginWrite(c);

        ImportBranch(legacy, c, tx, result);
        var categoryIds = ImportCategories(legacy, c, tx, nowDb, result);
        ImportProducts(legacy, c, tx, now, nowDb, categoryIds, result);
        ImportCustomers(legacy, c, tx, nowDb, result);
        ImportSuppliers(legacy, c, tx, nowDb, result);
        var employeeIds = ImportEmployees(legacy, c, tx, nowDb, result);
        ImportServices(legacy, c, tx, nowDb, result);
        ImportUsers(legacy, c, tx, nowDb, employeeIds, result);

        tx.Commit();
        result.Notes.Add("Transaksi lama (penjualan, registry, process, adjustment) sengaja TIDAK diimpor.");
        result.Notes.Add("Tandai produk yang dipakai sebagai bahan habis pakai di Master Produk, lalu isi resep di Master Jasa.");
        return result;
    }

    private static void RequireTables(SqliteConnection legacy, params string[] tables)
    {
        var existing = legacy.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table'").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = tables.Where(t => !existing.Contains(t)).ToList();
        if (missing.Count > 0)
            throw new BusinessException($"File ini bukan database MSPA Salon lama (tabel tidak ada: {string.Join(", ", missing)}).");
    }

    private static string? Text(object? v) => v is null or DBNull ? null : (string.IsNullOrWhiteSpace(v.ToString()) ? null : v.ToString()!.Trim());

    private static decimal Number(object? v) => v is null or DBNull ? 0m : Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture);

    private static bool Flag(object? v) => Number(v) != 0;

    private static string? MapGender(object? v) => Text(v)?.ToUpperInvariant() switch
    {
        "L" or "LAKI-LAKI" or "LAKI LAKI" or "PRIA" or "MALE" => Gender.Male,
        "P" or "PEREMPUAN" or "WANITA" or "FEMALE" => Gender.Female,
        _ => null,
    };

    /// <summary>Legacy money is REAL; the new model is whole Rupiah. Fractions are rounded and reported.</summary>
    private static decimal Rupiah(object? v, string what, ImportResult r)
    {
        var raw = Number(v);
        if (raw < 0) { r.Warnings.Add($"{what}: nilai negatif {raw} diganti 0."); return 0; }
        var rounded = Money.Round(raw);
        if (rounded != raw) r.Warnings.Add($"{what}: {raw} dibulatkan menjadi {rounded}.");
        return rounded;
    }

    private static IEnumerable<IDictionary<string, object?>> Rows(SqliteConnection legacy, string sql) =>
        legacy.Query(sql).Cast<IDictionary<string, object?>>();

    private static void ImportBranch(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, ImportResult r)
    {
        var b = Rows(legacy, "SELECT * FROM Tcabang WHERE ccode = 1").FirstOrDefault();
        if (b is null) { r.Warnings.Add("Data cabang (Tcabang) tidak ditemukan; pengaturan salon tidak diubah."); return; }
        var name = Text(b["cname"]) ?? Text(b["csname"]) ?? "MSPA Salon";
        c.Execute("UPDATE branch SET short_name = @s, name = @n, address = @a, phone = @p WHERE id = 1",
            new { s = Text(b["csname"]) ?? "", n = name, a = Text(b["alm1"]), p = Text(b["telp"]) }, tx);
        r.Counts["Pengaturan salon"] = 1;
    }

    private static Dictionary<string, long> ImportCategories(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, string now, ImportResult r)
    {
        var ids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows(legacy, "SELECT * FROM Tcateg WHERE ccode = 1 ORDER BY kodecateg"))
        {
            var e = new Category { Code = MasterValidators.NormalizeCode(Text(row["kodecateg"])), Name = Text(row["namacateg"]) ?? "" };
            if (!Valid(MasterValidators.Validate(e), $"Kategori {e.Code}", r)) continue;
            ids[e.Code] = c.ExecuteScalar<long>("INSERT INTO category(code, name) VALUES (@Code, @Name) RETURNING id", e, tx);
        }
        r.Counts["Kategori"] = ids.Count;
        return ids;
    }

    private static void ImportProducts(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, DateTime at, string now,
        Dictionary<string, long> categoryIds, ImportResult r)
    {
        int count = 0, opening = 0;
        foreach (var row in Rows(legacy, "SELECT * FROM mproduct WHERE ccode = 1 ORDER BY kodeprod"))
        {
            var code = MasterValidators.NormalizeCode(Text(row["kodeprod"]));
            var label = $"Produk {code}";
            var min = Math.Max(0, Number(row["stockmin"]));
            var max = Math.Max(0, Number(row["stockmax"]));
            if (max > 0 && max < min) { r.Warnings.Add($"{label}: stok maks < stok min, stok maks dikosongkan."); max = 0; }
            var p = new Product
            {
                Code = code, Name = Text(row["namaprod"]) ?? "", Unit = (Text(row["unit"]) ?? "PCS").ToUpperInvariant(),
                CostPrice = Rupiah(row["hargapokok"], $"{label} harga pokok", r),
                SellPrice = Rupiah(row["hargajual"], $"{label} harga jual", r),
                StockMin = min, StockMax = max,
                IsSellable = true, IsConsumable = false, IsActive = !Flag(row["discontinue"]),
            };
            if (Text(row["kodecateg"]) is { } cat)
            {
                if (categoryIds.TryGetValue(MasterValidators.NormalizeCode(cat), out var catId)) p.CategoryId = catId;
                else r.Warnings.Add($"{label}: kategori '{cat}' tidak ada, produk diimpor tanpa kategori.");
            }
            if (!Valid(MasterValidators.Validate(p), label, r)) continue;

            p.Id = c.ExecuteScalar<long>("""
                INSERT INTO product(code, name, category_id, unit, cost_price, sell_price, stock_min_x, stock_max_x,
                                    is_sellable, is_consumable, is_active, created_at, updated_at)
                VALUES (@Code, @Name, @CategoryId, @Unit, @cost, @sell, @minX, @maxX, 1, 0, @IsActive, @now, @now)
                RETURNING id
                """, new
            {
                p.Code, p.Name, p.CategoryId, p.Unit, cost = (long)p.CostPrice, sell = (long)p.SellPrice,
                minX = Quantity.ToStored(p.StockMin), maxX = Quantity.ToStored(p.StockMax), p.IsActive, now,
            }, tx);
            count++;

            // Opening balance enters through the ledger, so stock stays reconcilable from day one (fixes D9).
            var stock = Number(row["stockakhir"]);
            if (stock != 0)
            {
                StockLedger.Post(c, tx, p.Id, at, "OPENING", p.Id, 0, OpeningDocNo, stock, p.CostPrice,
                    "Saldo awal dari aplikasi lama");
                opening++;
                if (stock < 0) r.Warnings.Add($"{label}: stok awal negatif ({stock}) tetap diimpor apa adanya.");
            }
        }
        r.Counts["Produk"] = count;
        r.Counts["Saldo awal stok"] = opening;
    }

    private static void ImportCustomers(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, string now, ImportResult r)
    {
        var count = 0;
        foreach (var row in Rows(legacy, "SELECT * FROM mcustomer WHERE ccode = 1 ORDER BY kodecust"))
        {
            var e = new Customer
            {
                Code = MasterValidators.NormalizeCode(Text(row["kodecust"])), Name = Text(row["namacust"]) ?? "",
                Address = Text(row["alm"]), Phone = Text(row["hp"]), Gender = MapGender(row["jenis"]),
                IsMember = Flag(row["member"]), IsActive = !Flag(row["inactive"]),
            };
            if (e.Phone is not null && MasterValidators.Validate(new Customer { Code = "X", Name = "X", Phone = e.Phone }).Count > 0)
            {
                r.Warnings.Add($"Customer {e.Code}: no. HP '{e.Phone}' tidak valid, dikosongkan.");
                e.Phone = null;
            }
            if (IsWalkInName(e.Code, e.Name))
            {
                r.Notes.Add($"Customer lama {e.Code} '{e.Name}' adalah walk-in; dilebur ke customer 'UMUM / Walk-in' bawaan.");
                continue;
            }
            if (!Valid(MasterValidators.Validate(e), $"Customer {e.Code}", r)) continue;
            c.Execute("""
                INSERT INTO customer(code, name, phone, address, gender, is_member, is_walk_in, is_active, created_at, updated_at)
                VALUES (@Code, @Name, @Phone, @Address, @Gender, @IsMember, 0, @IsActive, @now, @now)
                """, new { e.Code, e.Name, e.Phone, e.Address, e.Gender, e.IsMember, e.IsActive, now }, tx);
            count++;
        }
        r.Counts["Customer"] = count;
    }

    private static void ImportSuppliers(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, string now, ImportResult r)
    {
        var count = 0;
        foreach (var row in Rows(legacy, "SELECT * FROM msupplier WHERE ccode = 1 ORDER BY kodesupp"))
        {
            var e = new Supplier
            {
                Code = MasterValidators.NormalizeCode(Text(row["kodesupp"])), Name = Text(row["namasupp"]) ?? "",
                Address = Text(row["alm"]), Phone = Text(row["hp"]) ?? Text(row["telp"]), ContactPerson = Text(row["contact"]),
                IsActive = !Flag(row["inactive"]),
            };
            if (!Valid(MasterValidators.Validate(e), $"Supplier {e.Code}", r)) continue;
            c.Execute("""
                INSERT INTO supplier(code, name, phone, address, contact_person, is_active, created_at, updated_at)
                VALUES (@Code, @Name, @Phone, @Address, @ContactPerson, @IsActive, @now, @now)
                """, new { e.Code, e.Name, e.Phone, e.Address, e.ContactPerson, e.IsActive, now }, tx);
            count++;
        }
        r.Counts["Supplier"] = count;
    }

    private static Dictionary<string, long> ImportEmployees(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, string now, ImportResult r)
    {
        var ids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows(legacy, "SELECT * FROM tkar WHERE ccode = 1 ORDER BY kodekar"))
        {
            var e = new Employee
            {
                Code = MasterValidators.NormalizeCode(Text(row["kodekar"])), Name = Text(row["namakar"]) ?? "",
                Gender = MapGender(row["jenis"]), Position = Text(row["jabatan"]), Phone = Text(row["telp"]),
                Address = Text(row["alm"]), IsCapster = Flag(row["capster"]), IsActive = !Flag(row["inactive"]),
            };
            if (e.Phone is not null && MasterValidators.Validate(new Employee { Code = "X", Name = "X", Phone = e.Phone }).Count > 0)
            {
                r.Warnings.Add($"Karyawan {e.Code}: no. telp '{e.Phone}' tidak valid, dikosongkan.");
                e.Phone = null;
            }
            if (!Valid(MasterValidators.Validate(e), $"Karyawan {e.Code}", r)) continue;
            ids[e.Code] = c.ExecuteScalar<long>("""
                INSERT INTO employee(code, name, gender, position, phone, address, is_capster, is_active, created_at, updated_at)
                VALUES (@Code, @Name, @Gender, @Position, @Phone, @Address, @IsCapster, @IsActive, @now, @now) RETURNING id
                """, new { e.Code, e.Name, e.Gender, e.Position, e.Phone, e.Address, e.IsCapster, e.IsActive, now }, tx);
        }
        r.Counts["Karyawan"] = ids.Count;
        return ids;
    }

    private static void ImportServices(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, string now, ImportResult r)
    {
        var count = 0;
        foreach (var row in Rows(legacy, "SELECT * FROM Tservice WHERE ccode = 1 ORDER BY kodeservice"))
        {
            var code = MasterValidators.NormalizeCode(Text(row["kodeservice"]));
            var e = new Service
            {
                Code = code, Name = Text(row["namaservice"]) ?? "",
                Price = Rupiah(row["harga"], $"Jasa {code} harga", r),
                Commission = Rupiah(row["komisi"], $"Jasa {code} komisi", r),
            };
            if (e.Commission > e.Price)
            {
                r.Warnings.Add($"Jasa {code}: komisi {e.Commission} > harga {e.Price}, komisi disamakan dengan harga.");
                e.Commission = e.Price;
            }
            if (!Valid(MasterValidators.Validate(e), $"Jasa {code}", r)) continue;
            c.Execute("""
                INSERT INTO service(code, name, price, commission, is_active, created_at, updated_at)
                VALUES (@Code, @Name, @price, @commission, 1, @now, @now)
                """, new { e.Code, e.Name, price = (long)e.Price, commission = (long)e.Commission, now }, tx);
            count++;
        }
        r.Counts["Jasa"] = count;
        if (count > 0) r.Notes.Add("Resep bahan jasa belum ada di aplikasi lama; isi di Master Jasa agar backflush berjalan.");
    }

    private static void ImportUsers(SqliteConnection legacy, SqliteConnection c, SqliteTransaction tx, string now,
        Dictionary<string, long> employeeIds, ImportResult r)
    {
        var adminRole = c.ExecuteScalar<long>("SELECT id FROM role WHERE code = @c", new { c = RoleCodes.Admin }, tx);
        var cashierRole = c.ExecuteScalar<long>("SELECT id FROM role WHERE code = @c", new { c = RoleCodes.Cashier }, tx);
        var count = 0;
        foreach (var row in Rows(legacy, "SELECT * FROM users WHERE ccode = 1 ORDER BY username"))
        {
            var username = Text(row["username"]) ?? "";
            var password = row["pwd"] is null or DBNull ? "" : row["pwd"]!.ToString()!;
            if (password.Length == 0) { r.Warnings.Add($"User '{username}': password kosong, dilewati."); continue; }
            if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE username = @u COLLATE NOCASE", new { u = username }, tx) > 0)
            {
                r.Notes.Add($"User '{username}' sudah ada di aplikasi baru; data lama dilewati.");
                continue;
            }
            // Legacy access: 'SA' / 'ADMIN' → ADMIN; anything else → KASIR (decision A4: SA = Admin).
            var access = (Text(row["pa"]) ?? "").ToUpperInvariant();
            var roleId = access is "SA" or "ADMIN" ? adminRole : cashierRole;
            var empCode = MasterValidators.NormalizeCode(Text(row["kodekar"]));
            long? employeeId = employeeIds.TryGetValue(empCode, out var eid) ? eid : null;
            var user = new AppUser { Username = username, DisplayName = username, RoleId = roleId };
            if (!Valid(MasterValidators.Validate(user), $"User '{username}'", r)) continue;

            var h = PasswordHasher.Hash(password);
            c.Execute("""
                INSERT INTO app_user(username, display_name, employee_id, role_id, password_hash, password_salt,
                                     password_iterations, is_active, must_change_password, created_at, updated_at)
                VALUES (@username, @username, @employeeId, @roleId, @h, @s, @i, 1, 1, @now, @now)
                """, new { username, employeeId, roleId, h = h.Hash, s = h.Salt, i = h.Iterations, now }, tx);
            count++;
        }
        r.Counts["User"] = count;
        if (count > 0) r.Notes.Add("Password user lama sudah di-hash; setiap user wajib mengganti password saat login pertama.");
    }

    /// <summary>Legacy data used an ordinary customer such as "UMUM / WALK-IN"; the new model has a built-in walk-in customer.</summary>
    public static bool IsWalkInName(string code, string name)
    {
        var n = name.ToUpperInvariant().Replace(" ", "").Replace("-", "");
        return code == "UMUM" || n.Contains("WALKIN") || n is "UMUM" or "UMUM/" or "UMUM/WALKIN";
    }

    private static bool Valid(List<string> errors, string label, ImportResult r)
    {
        if (errors.Count == 0) return true;
        r.Warnings.Add($"{label} dilewati: {string.Join(" ", errors)}");
        return false;
    }
}
