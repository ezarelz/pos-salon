using Dapper;
using Microsoft.Data.Sqlite;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;
using MSPASalon.Core.Interfaces;
using MSPASalon.Core.Validation;
using MSPASalon.Infrastructure.Database;

namespace MSPASalon.Infrastructure.Services;

public sealed class CustomerService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Customer>(db, clock)
{
    protected override string Table => "customer";
    protected override string CodePrefix => "CST";
    protected override string EntityLabel => "Customer";

    private const string Select = """
        SELECT id, code, name, phone, address, gender, is_member, is_walk_in, is_active, notes FROM customer
        """;

    public override IReadOnlyList<Customer> GetAll()
    {
        using var c = Db.Open();
        return c.Query<Customer>(Select + " ORDER BY is_walk_in DESC, code").ToList();
    }

    public override Customer? Get(long id)
    {
        using var c = Db.Open();
        return c.QuerySingleOrDefault<Customer>(Select + " WHERE id = @id", new { id });
    }

    protected override void Normalize(Customer e)
    {
        base.Normalize(e);
        e.Phone = Clean(e.Phone); e.Address = Clean(e.Address); e.Notes = Clean(e.Notes);
        e.Gender = Clean(e.Gender);
    }

    protected override List<string> ValidateFields(Customer e) => MasterValidators.Validate(e);

    protected override void ValidateWithDatabase(SqliteConnection c, SqliteTransaction tx, Customer e, List<string> errors)
    {
        // There must always be one active walk-in customer (default customer for product sales, rule S1).
        if (e.Id > 0 && (!e.IsWalkIn || !e.IsActive) &&
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM customer WHERE is_walk_in = 1 AND is_active = 1 AND id <> @id", new { id = e.Id }, tx) == 0 &&
            c.ExecuteScalar<long>("SELECT is_walk_in FROM customer WHERE id = @id", new { id = e.Id }, tx) == 1)
            errors.Add("Harus ada minimal satu customer Walk-in yang aktif.");
    }

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Customer e, string now) =>
        c.ExecuteScalar<long>("""
            INSERT INTO customer(code, name, phone, address, gender, is_member, is_walk_in, is_active, notes, created_at, updated_at)
            VALUES (@Code, @Name, @Phone, @Address, @Gender, @IsMember, @IsWalkIn, @IsActive, @Notes, @now, @now)
            RETURNING id
            """, new { e.Code, e.Name, e.Phone, e.Address, e.Gender, e.IsMember, e.IsWalkIn, e.IsActive, e.Notes, now }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Customer e, string now) =>
        c.Execute("""
            UPDATE customer SET code = @Code, name = @Name, phone = @Phone, address = @Address, gender = @Gender,
                   is_member = @IsMember, is_walk_in = @IsWalkIn, is_active = @IsActive, notes = @Notes, updated_at = @now
            WHERE id = @Id
            """, new { e.Id, e.Code, e.Name, e.Phone, e.Address, e.Gender, e.IsMember, e.IsWalkIn, e.IsActive, e.Notes, now }, tx);

    protected override void BeforeDelete(SqliteConnection c, SqliteTransaction tx, long id)
    {
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM customer WHERE id = @id AND is_walk_in = 1", new { id }, tx) == 1 &&
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM customer WHERE is_walk_in = 1 AND is_active = 1 AND id <> @id", new { id }, tx) == 0)
            throw new BusinessException("Customer Walk-in terakhir tidak bisa dihapus.");
    }
}

public sealed class SupplierService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Supplier>(db, clock)
{
    protected override string Table => "supplier";
    protected override string CodePrefix => "SUP";
    protected override string EntityLabel => "Supplier";

    private const string Select = "SELECT id, code, name, phone, address, contact_person, is_active FROM supplier";

    public override IReadOnlyList<Supplier> GetAll()
    {
        using var c = Db.Open();
        return c.Query<Supplier>(Select + " ORDER BY code").ToList();
    }

    public override Supplier? Get(long id)
    {
        using var c = Db.Open();
        return c.QuerySingleOrDefault<Supplier>(Select + " WHERE id = @id", new { id });
    }

    protected override void Normalize(Supplier e)
    {
        base.Normalize(e);
        e.Phone = Clean(e.Phone); e.Address = Clean(e.Address); e.ContactPerson = Clean(e.ContactPerson);
    }

    protected override List<string> ValidateFields(Supplier e) => MasterValidators.Validate(e);

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Supplier e, string now) =>
        c.ExecuteScalar<long>("""
            INSERT INTO supplier(code, name, phone, address, contact_person, is_active, created_at, updated_at)
            VALUES (@Code, @Name, @Phone, @Address, @ContactPerson, @IsActive, @now, @now) RETURNING id
            """, new { e.Code, e.Name, e.Phone, e.Address, e.ContactPerson, e.IsActive, now }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Supplier e, string now) =>
        c.Execute("""
            UPDATE supplier SET code = @Code, name = @Name, phone = @Phone, address = @Address,
                   contact_person = @ContactPerson, is_active = @IsActive, updated_at = @now WHERE id = @Id
            """, new { e.Id, e.Code, e.Name, e.Phone, e.Address, e.ContactPerson, e.IsActive, now }, tx);
}

public sealed class CategoryService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Category>(db, clock)
{
    protected override string Table => "category";
    protected override string CodePrefix => "KTG";
    protected override string EntityLabel => "Kategori";

    public override IReadOnlyList<Category> GetAll()
    {
        using var c = Db.Open();
        return c.Query<Category>("SELECT id, code, name FROM category ORDER BY code").ToList();
    }

    public override Category? Get(long id)
    {
        using var c = Db.Open();
        return c.QuerySingleOrDefault<Category>("SELECT id, code, name FROM category WHERE id = @id", new { id });
    }

    protected override List<string> ValidateFields(Category e) => MasterValidators.Validate(e);

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Category e, string now) =>
        c.ExecuteScalar<long>("INSERT INTO category(code, name) VALUES (@Code, @Name) RETURNING id", new { e.Code, e.Name }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Category e, string now) =>
        c.Execute("UPDATE category SET code = @Code, name = @Name WHERE id = @Id", new { e.Id, e.Code, e.Name }, tx);

    public override string? DeleteWarning(long id)
    {
        using var c = Db.Open();
        var n = c.ExecuteScalar<long>("SELECT COUNT(*) FROM product WHERE category_id = @id", new { id });
        return n > 0 ? $"{n} produk akan menjadi tanpa kategori." : null;
    }
}

public sealed class EmployeeService(DbConnectionFactory db, IClock clock) : MasterServiceBase<Employee>(db, clock)
{
    protected override string Table => "employee";
    protected override string CodePrefix => "KAR";
    protected override string EntityLabel => "Karyawan";

    private const string Select = "SELECT id, code, name, gender, position, phone, address, is_capster, is_active FROM employee";

    public override IReadOnlyList<Employee> GetAll()
    {
        using var c = Db.Open();
        return c.Query<Employee>(Select + " ORDER BY code").ToList();
    }

    public override Employee? Get(long id)
    {
        using var c = Db.Open();
        return c.QuerySingleOrDefault<Employee>(Select + " WHERE id = @id", new { id });
    }

    protected override void Normalize(Employee e)
    {
        base.Normalize(e);
        e.Phone = Clean(e.Phone); e.Address = Clean(e.Address); e.Position = Clean(e.Position); e.Gender = Clean(e.Gender);
    }

    protected override List<string> ValidateFields(Employee e) => MasterValidators.Validate(e);

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, Employee e, string now) =>
        c.ExecuteScalar<long>("""
            INSERT INTO employee(code, name, gender, position, phone, address, is_capster, is_active, created_at, updated_at)
            VALUES (@Code, @Name, @Gender, @Position, @Phone, @Address, @IsCapster, @IsActive, @now, @now) RETURNING id
            """, new { e.Code, e.Name, e.Gender, e.Position, e.Phone, e.Address, e.IsCapster, e.IsActive, now }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, Employee e, string now) =>
        c.Execute("""
            UPDATE employee SET code = @Code, name = @Name, gender = @Gender, position = @Position, phone = @Phone,
                   address = @Address, is_capster = @IsCapster, is_active = @IsActive, updated_at = @now WHERE id = @Id
            """, new { e.Id, e.Code, e.Name, e.Gender, e.Position, e.Phone, e.Address, e.IsCapster, e.IsActive, now }, tx);

    public override string? DeleteWarning(long id)
    {
        using var c = Db.Open();
        var users = c.ExecuteScalar<long>("SELECT COUNT(*) FROM app_user WHERE employee_id = @id", new { id });
        return users > 0 ? "User yang terhubung ke karyawan ini akan dilepas (user tetap ada)." : null;
    }
}

public sealed class PaymentMethodService(DbConnectionFactory db, IClock clock) : MasterServiceBase<PaymentMethod>(db, clock)
{
    protected override string Table => "payment_method";
    protected override string CodePrefix => "BYR";
    protected override string EntityLabel => "Metode bayar";

    private const string Select = "SELECT id, code, name, is_cash, is_active, sort_order FROM payment_method";

    public override IReadOnlyList<PaymentMethod> GetAll()
    {
        using var c = Db.Open();
        return c.Query<PaymentMethod>(Select + " ORDER BY sort_order, code").ToList();
    }

    public override PaymentMethod? Get(long id)
    {
        using var c = Db.Open();
        return c.QuerySingleOrDefault<PaymentMethod>(Select + " WHERE id = @id", new { id });
    }

    protected override List<string> ValidateFields(PaymentMethod e) => MasterValidators.Validate(e);

    protected override void ValidateWithDatabase(SqliteConnection c, SqliteTransaction tx, PaymentMethod e, List<string> errors)
    {
        if (e.Id > 0 && (!e.IsActive || !e.IsCash) &&
            c.ExecuteScalar<long>("SELECT COUNT(*) FROM payment_method WHERE is_cash = 1 AND is_active = 1 AND id <> @id", new { id = e.Id }, tx) == 0)
            errors.Add("Harus ada minimal satu metode bayar Tunai yang aktif.");
    }

    protected override long Insert(SqliteConnection c, SqliteTransaction tx, PaymentMethod e, string now) =>
        c.ExecuteScalar<long>("""
            INSERT INTO payment_method(code, name, is_cash, is_active, sort_order)
            VALUES (@Code, @Name, @IsCash, @IsActive, @SortOrder) RETURNING id
            """, new { e.Code, e.Name, e.IsCash, e.IsActive, e.SortOrder }, tx);

    protected override void Update(SqliteConnection c, SqliteTransaction tx, PaymentMethod e, string now) =>
        c.Execute("""
            UPDATE payment_method SET code = @Code, name = @Name, is_cash = @IsCash, is_active = @IsActive, sort_order = @SortOrder
            WHERE id = @Id
            """, new { e.Id, e.Code, e.Name, e.IsCash, e.IsActive, e.SortOrder }, tx);

    protected override void BeforeDelete(SqliteConnection c, SqliteTransaction tx, long id)
    {
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM payment_method WHERE is_cash = 1 AND is_active = 1 AND id <> @id", new { id }, tx) == 0)
            throw new BusinessException("Metode bayar Tunai terakhir tidak bisa dihapus.");
    }
}
