# MSPA Salon

Aplikasi desktop manajemen salon (Windows): master data, registrasi customer → proses treatment → pembayaran,
backflush bahan habis pakai, penjualan produk, dan inventori berbasis stock ledger.
Dibangun ulang dari nol menggantikan aplikasi MSPA Salon lama (lihat `docs/PHASE1_DISCOVERY.md`).

**Teknologi:** .NET 8 · C# 12 · WPF · SQLite · Dapper · CommunityToolkit.Mvvm

**Status: Milestone M4c** (lihat `CHANGELOG.md`): 8 laporan (Rekap Kas, Pendapatan, Komisi, Jasa & Margin, Produk & Laba Kotor, Stok, Pembatalan, Kunjungan Customer) + export Excel/PDF, format nota + logo, dashboard harian, mode gelap, master data, user & role, login, import data lama, backup, shell responsif,
registrasi → proses treatment → pembayaran + backflush + nota 58 mm, penjualan, pembelian, stock adjustment, kartu stok.
Laporan & export menyusul di M4. Rencana lengkap: `docs/TECHNICAL_ARCHITECTURE.md` §6.

---

## 1. Prasyarat

| Kebutuhan | Keterangan |
|---|---|
| Windows 10/11 | Untuk **menjalankan** aplikasi (WPF hanya berjalan di Windows) |
| .NET 8 SDK (x64) | `winget install Microsoft.DotNet.SDK.8` atau https://dotnet.microsoft.com/download/dotnet/8.0 |
| Visual Studio Code | + ekstensi **C# Dev Kit** (`ms-dotnettools.csdevkit`) |
| Git | Opsional |

Cek: `dotnet --list-sdks` → harus ada `8.0.x`.

## 2. Build & Run (Windows, terminal PowerShell di VS Code)

```powershell
cd C:\dev\MSPASalon          # folder hasil ekstrak / clone
dotnet restore
dotnet build
dotnet run --project src/MSPASalon.Desktop/MSPASalon.Desktop.csproj
```

Login pertama: **admin / admin** → Anda langsung diminta mengganti password.

Opsi database lain: `dotnet run --project src/MSPASalon.Desktop/MSPASalon.Desktop.csproj -- --db "D:\data\salon-uji.db"`

## 3. Test otomatis

```powershell
dotnet run --project tests/MSPASalon.Tests            # semua test
dotnet run --project tests/MSPASalon.Tests -- INT-    # hanya test integrasi
```

Exit code 0 = semua lolos. Laporan: `tests/TestResults/test-results.md`. Daftar kasus uji: `docs/TEST_CASES.md`.
Test tidak membutuhkan WPF, jadi juga bisa dijalankan di WSL/Linux.

## 4. Debug di VS Code

1. Buka folder `MSPASalon` di VS Code (`code .`).
2. Tab **Run and Debug** → pilih **MSPA Salon (Desktop)** → F5. Konfigurasi ada di `.vscode/launch.json`.
3. Konfigurasi **MSPA Salon (Tests)** menjalankan test runner dengan debugger.

## 5. Build dari WSL (opsional)

Proyek WPF memakai `EnableWindowsTargeting=true`, sehingga bisa **di-compile** dari WSL/Linux, tetapi **tidak bisa dijalankan** di sana.

* Gunakan .NET SDK resmi Microsoft (`dotnet-install.sh` atau repo packages.microsoft.com). SDK *source-built* dari paket Ubuntu (`apt install dotnet-sdk-8.0`) **tidak** menyertakan `Microsoft.NET.Sdk.WindowsDesktop`, sehingga proyek WPF gagal dimuat. Proyek lain (Core, Infrastructure, Presentation, Tests, Import) tetap bisa.
* Simpan repo di sisi Windows (mis. `C:\dev\MSPASalon`) dan jalankan aplikasi dari PowerShell. Build dari `/mnt/c/...` di WSL berfungsi, tetapi lebih lambat.

## 6. Database

| Item | Nilai |
|---|---|
| Lokasi default | `%LOCALAPPDATA%\MSPASalon\mspasalon.db` |
| Override | argumen `--db <path>` atau environment variable `MSPASALON_DB` |
| Log error | `%LOCALAPPDATA%\MSPASalon\logs\error-yyyyMMdd.log` |
| Skema | `docs/DATABASE_SCHEMA.md`; migrasi otomatis saat aplikasi start |

**Backup:** menu **Backup Database** (pojok kiri bawah) → pilih folder → `mspasalon_yyyyMMdd_HHmmss.db`. Aman dilakukan saat aplikasi berjalan.

**Restore:** tutup aplikasi → salin file backup ke `%LOCALAPPDATA%\MSPASalon\` dengan nama `mspasalon.db` → hapus `mspasalon.db-wal` dan `mspasalon.db-shm` lama bila ada → buka aplikasi.

## 7. Import data dari aplikasi lama

Hanya **master data** (cabang, kategori, produk + saldo awal stok, customer, supplier, karyawan, jasa, user) dan hanya ke database yang masih kosong. Transaksi lama tidak diimpor. File lama dibuka **read-only**.

* Dari aplikasi: menu **Import Data Lama** → pilih `salon.sqlite`.
* Dari terminal:
  ```powershell
  dotnet run --project tools/MSPASalon.Import -- "C:\MSPA Lama\salon.sqlite"
  ```

Setelah import: tandai produk bahan habis pakai (Master Produk), lalu isi resep bahan per jasa (Master Jasa). User lama wajib mengganti password saat login pertama.

## 8. Struktur

```
src/MSPASalon.Core            domain, aturan uang/qty, validasi, permission, kontrak
src/MSPASalon.Infrastructure  SQLite/Dapper, migrasi, penomoran, stock ledger, backup, import
src/MSPASalon.Presentation    ViewModel + aturan ukuran jendela (tanpa WPF, dites otomatis)
src/MSPASalon.Desktop         WPF: Views, tema, converter, dialog
tests/MSPASalon.Tests         unit + integrasi + ViewModel tests
tools/MSPASalon.Import        CLI import data lama
docs/                         BUSINESS_RULES, DATABASE_SCHEMA, TECHNICAL_ARCHITECTURE, TEST_CASES, UAT, discovery
```

## 9. Dokumentasi

| Dokumen | Isi |
|---|---|
| `docs/BUSINESS_RULES.md` | Aturan bisnis, keputusan (K1–K18), asumsi |
| `docs/PHASE1_DISCOVERY.md` | Hasil analisis aplikasi lama dan 17 defect |
| `docs/DATABASE_SCHEMA.md` | Skema, constraint, ledger, backup/restore |
| `docs/TECHNICAL_ARCHITECTURE.md` | Arsitektur, pilihan teknologi, responsivitas, milestone |
| `docs/TEST_CASES.md` | Kasus uji dan hasil |
| `docs/UAT_RESPONSIVE_CHECKLIST.md` | Checklist uji tampilan di Windows |
