using System.Text.RegularExpressions;
using MSPASalon.Core.Domain;
using MSPASalon.Core.Entities;

namespace MSPASalon.Core.Validation;

/// <summary>
/// Field-level business validation for master data. Returns user-facing Indonesian messages.
/// Rules that need the database (code uniqueness, recipe product flags) are enforced in the services.
/// </summary>
public static partial class MasterValidators
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 100;
    public const int MinPasswordLength = 6;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._\-/]*$")]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"^[0-9+\-() ]{3,25}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._\-]{3,30}$")]
    private static partial Regex UsernameRegex();

    /// <summary>Codes are stored trimmed and upper-case so "cst001" and "CST001" are the same record.</summary>
    public static string NormalizeCode(string? code) => (code ?? "").Trim().ToUpperInvariant();

    public static void Code(List<string> errors, string? code, string label = "Kode")
    {
        var c = NormalizeCode(code);
        if (c.Length == 0) errors.Add($"{label} wajib diisi.");
        else if (c.Length > CodeMaxLength) errors.Add($"{label} maksimal {CodeMaxLength} karakter.");
        else if (!CodeRegex().IsMatch(c)) errors.Add($"{label} hanya boleh huruf, angka, titik, garis bawah, strip, atau garis miring, tanpa spasi.");
    }

    public static void Name(List<string> errors, string? name, string label = "Nama")
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) errors.Add($"{label} wajib diisi.");
        else if (n.Length > NameMaxLength) errors.Add($"{label} maksimal {NameMaxLength} karakter.");
    }

    public static void Phone(List<string> errors, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(phone) && !PhoneRegex().IsMatch(phone.Trim()))
            errors.Add("No. HP/Telp hanya boleh angka, spasi, +, -, dan tanda kurung (3–25 karakter).");
    }

    public static void GenderValue(List<string> errors, string? gender)
    {
        if (gender is not (null or "" or Gender.Male or Gender.Female))
            errors.Add("Jenis kelamin tidak valid.");
    }

    public static void Rupiah(List<string> errors, decimal value, string label)
    {
        if (value < 0) errors.Add($"{label} tidak boleh negatif.");
        else if (!Money.IsWholeRupiah(value)) errors.Add($"{label} harus Rupiah bulat.");
    }

    public static void Qty(List<string> errors, decimal value, string label, bool mustBePositive)
    {
        if (mustBePositive ? value <= 0 : value < 0)
            errors.Add(mustBePositive ? $"{label} harus lebih dari 0." : $"{label} tidak boleh negatif.");
        else if (!Quantity.HasValidPrecision(value))
            errors.Add($"{label} maksimal {Quantity.Decimals} angka desimal.");
    }

    public static List<string> Validate(Customer c)
    {
        var e = new List<string>();
        Code(e, c.Code); Name(e, c.Name); Phone(e, c.Phone); GenderValue(e, c.Gender);
        return e;
    }

    public static List<string> Validate(Supplier s)
    {
        var e = new List<string>();
        Code(e, s.Code); Name(e, s.Name); Phone(e, s.Phone);
        return e;
    }

    public static List<string> Validate(Category c)
    {
        var e = new List<string>();
        Code(e, c.Code); Name(e, c.Name);
        return e;
    }

    public static List<string> Validate(Employee m)
    {
        var e = new List<string>();
        Code(e, m.Code); Name(e, m.Name); Phone(e, m.Phone); GenderValue(e, m.Gender);
        return e;
    }

    public static List<string> Validate(Product p)
    {
        var e = new List<string>();
        Code(e, p.Code); Name(e, p.Name);
        if (string.IsNullOrWhiteSpace(p.Unit)) e.Add("Satuan stok wajib diisi.");
        else if (p.Unit.Trim().Length > 10) e.Add("Satuan stok maksimal 10 karakter.");
        Rupiah(e, p.CostPrice, "Harga pokok");
        Rupiah(e, p.SellPrice, "Harga jual");
        Qty(e, p.StockMin, "Stok minimum", mustBePositive: false);
        Qty(e, p.StockMax, "Stok maksimum", mustBePositive: false);
        if (p.StockMax > 0 && p.StockMax < p.StockMin) e.Add("Stok maksimum tidak boleh lebih kecil dari stok minimum.");
        if (!p.IsSellable && !p.IsConsumable) e.Add("Produk harus ditandai minimal sebagai 'Dijual' atau 'Bahan habis pakai'.");
        if (p.ContentQty.HasValue)
        {
            Qty(e, p.ContentQty.Value, "Isi per satuan", mustBePositive: true);
            if (string.IsNullOrWhiteSpace(p.ContentUnit)) e.Add("Satuan isi wajib diisi bila isi per satuan diisi (mis. ml).");
        }
        return e;
    }

    public static List<string> Validate(Service s)
    {
        var e = new List<string>();
        Code(e, s.Code); Name(e, s.Name);
        Rupiah(e, s.Price, "Harga jasa");
        Rupiah(e, s.Commission, "Komisi");
        if (s.Commission > s.Price) e.Add("Komisi tidak boleh lebih besar dari harga jasa.");

        var seen = new HashSet<long>();
        foreach (var m in s.Materials)
        {
            var label = string.IsNullOrWhiteSpace(m.ProductName) ? "Bahan" : $"Bahan '{m.ProductName}'";
            if (m.ProductId <= 0) e.Add("Ada baris resep tanpa produk.");
            else if (!seen.Add(m.ProductId)) e.Add($"{label} tercantum lebih dari sekali di resep.");
            Qty(e, m.Qty, $"Qty pemakaian {label}", mustBePositive: true);
        }
        return e;
    }

    public static List<string> Validate(PaymentMethod p)
    {
        var e = new List<string>();
        Code(e, p.Code); Name(e, p.Name);
        return e;
    }

    public static List<string> Validate(Role r)
    {
        var e = new List<string>();
        Code(e, r.Code); Name(e, r.Name);
        var known = Permissions.All.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (r.Permissions.Count == 0 && !string.Equals(r.Code, RoleCodes.Admin, StringComparison.OrdinalIgnoreCase))
            e.Add("Pilih minimal satu hak akses.");
        foreach (var p in r.Permissions.Where(p => !known.Contains(p))) e.Add($"Hak akses tidak dikenal: {p}.");
        return e;
    }

    public static List<string> Validate(AppUser u)
    {
        var e = new List<string>();
        if (!UsernameRegex().IsMatch((u.Username ?? "").Trim()))
            e.Add("Username 3–30 karakter: huruf, angka, titik, garis bawah, atau strip.");
        Name(e, u.DisplayName, "Nama tampilan");
        if (u.RoleId <= 0) e.Add("Role wajib dipilih.");
        return e;
    }

    public static List<string> ValidatePassword(string? password)
    {
        var e = new List<string>();
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            e.Add($"Password minimal {MinPasswordLength} karakter.");
        return e;
    }

    public static List<string> Validate(Branch b)
    {
        var e = new List<string>();
        Name(e, b.Name, "Nama salon");
        if (b.PaperWidthMm is not (58 or 80)) e.Add("Lebar kertas nota harus 58 atau 80 mm.");
        Phone(e, b.Phone);
        return e;
    }
}
