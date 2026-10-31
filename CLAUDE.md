# CLAUDE.md — panduan kerja untuk AI/developer

Baca file ini sebelum mengubah kode. Sumber kebenaran aturan bisnis: `docs/BUSINESS_RULES.md`.
Riwayat perubahan: `CHANGELOG.md`. Daftar test: `docs/TEST_CASES.md`.

## Repo & kustomisasi pengguna
- Sumber kebenaran kode: https://github.com/ezarelz/pos-salon (branch utama). Sesi baru: clone repo ini, jangan pakai salinan lama.
- Nama tampil aplikasi: **POS Salon** (judul window/login/dialog, Product/Company). AssemblyName tetap `MSPASalon`,
  folder data tetap `%LOCALAPPDATA%\MSPASalon\`, namespace/proyek tetap `MSPASalon.*`.
- Ikon: `src/MSPASalon.Desktop/Assets/app.ico` (16–256 px) via `<ApplicationIcon>`.
- Judul nota treatment diubah pengguna menjadi "Faktur Penjualan"; kartu dashboard "Kinerja karyawan hari ini".

## Status
- Selesai: M1 (master, user/role, login, import data lama, backup, shell responsif), M2 (registrasi → treatment →
  pembayaran, backflush, nota 58 mm), M3 (penjualan, pembelian, stock adjustment, kartu stok),
  M3.1 (dashboard harian, mode gelap), M3.2 (format nota `Nama x qty  harga` + logo),
  **M4a** (migrasi 003, izin laporan, kerangka laporan + export xlsx/PDF, Rekap Kas Harian, Pendapatan, Komisi Capster),
  **M4b–M4c** (Jasa & Margin, Produk & Laba Kotor, Stok, Pembatalan, Kunjungan Customer + riwayat & penanda registrasi).
- Berikutnya: **M4d** (Reset Data Transaksi + Restore Backup) — konfirmasi pengguna dulu sebelum mulai.
- Upgrade .NET 10 ditunda; fokus .NET 8.

## Stack & struktur
- .NET 8, C# 12, WPF, SQLite (Microsoft.Data.Sqlite 8.0.10), Dapper 2.1.35, CommunityToolkit.Mvvm 8.2.2 (tanpa source generator).
- `src/MSPASalon.Core` — entity, aturan domain murni (Money, Quantity, ReceiptBuilder, TransactionRules), interface service.
- `src/MSPASalon.Infrastructure` — SQLite, migrasi (`Database/Migrations/NNN_*.sql`, embedded), service.
- `src/MSPASalon.Presentation` — ViewModel **tanpa referensi WPF** (bisa dites di Linux).
- `src/MSPASalon.Desktop` — WPF (view, tema, print, dialog). `EnableWindowsTargeting=true` hanya di csproj ini.
- `tests/MSPASalon.Tests` — test runner console: `dotnet run --project tests/MSPASalon.Tests` (filter: `-- DASH-`, `-- M2-`, …).

## Aturan data (jangan dilanggar)
- Uang = INTEGER Rupiah bulat (`Money.ToStored/FromStored`). Qty = INTEGER ×10.000 (4 desimal, `Quantity`).
- Tanggal disimpan TEXT `yyyy-MM-dd HH:mm:ss` (`DbTime`); petakan lewat row class string, jangan Dapper → DateTime langsung.
- Transaksi tulis: `DbConnectionFactory.BeginWrite` (BEGIN IMMEDIATE), satu dokumen = satu transaksi DB, rollback total bila gagal.
- Nomor dokumen dari UPSERT…RETURNING di dalam transaksi pemanggil (tanpa loncat). Format `YYMM/KODE/0001`; nomor treatment = nomor registrasi.
- Stock ledger = sumber kebenaran; `product.stock_on_hand_x` hanya cache. Semua mutasi stok lewat `StockLedger.Post`.
- Batal dokumen = status BATAL + hapus baris ledger & payment dokumen itu (K11); nomor tidak dipakai ulang.
- Komisi: snapshot dari master jasa saat treatment LUNAS, per baris jasa, untuk capster final; batal → 0.
- Perubahan skema: tambah file migrasi baru (`003_...sql`), jangan ubah `001`/`002`. Test INT-01 menguji versi terbaru + jalur upgrade.

## Aturan WPF/XAML
- Warna hanya di `Themes/Palette.Light.xaml` & `Palette.Dark.xaml` (kunci identik) dan dirujuk dengan **DynamicResource**.
  StaticResource ke kunci palet = tidak ikut ganti tema.
- Converter **bukan resource**: pakai `{x:Static conv:Conv.Nama}` (`Converters/Converters.cs`).
- Merged dictionary: Palette → Theme → EditorTemplates → PageTemplates; StaticResource hanya ke kunci di kamus sebelumnya.
  `App.xaml` tidak boleh mendeklarasikan resource langsung.
- Layout responsif: header Auto · konten ScrollViewer * · tombol aksi Auto (selalu terlihat). Target terkecil 853×440 DIP.
- Halaman baru: ViewModel di Presentation + DataTemplate di `PageTemplates.xaml` + daftar di `PageTemplateSelector`.

## Batasan lingkungan AI
- Sandbox AI (Linux) **tidak bisa** mem-build/menjalankan proyek WPF. Yang bisa diverifikasi di sana: build Core/
  Infrastructure/Presentation/Tests + seluruh test + cek statis XAML. Tampilan WPF & printer diuji pengguna di Windows.
- Jangan pernah menyatakan build/test berhasil bila belum benar-benar dijalankan.
- Repo publik: jangan masukkan database lama (`salon.sqlite`), binary, kode hasil dekompilasi, atau data pribadi
  (nama orang, nama salon asli, password lama). Data uji memakai nama generik.

## Konvensi kerja
- Bahasa UI & dokumen: Bahasa Indonesia formal. Kode & komentar: Inggris.
- Asumsi baru dicatat di BUSINESS_RULES (log keputusan K-nomor / asumsi A-nomor), bukan diputuskan diam-diam.
- Setiap fitur: test di `tests/` + baris di `docs/TEST_CASES.md` + skenario singkat di `docs/UAT_RESPONSIVE_CHECKLIST.md`
  (Test Step ringkas, Expected Result satu baris) + entri `CHANGELOG.md`.

## M4 — Laporan (spesifikasi disetujui: `docs/BUSINESS_RULES.md` v0.6 §14–16, aturan L1–L10, K20–K25, R1–R5, T1–T4)
| Tahap | Isi | Status |
|---|---|---|
| M4a | Migrasi 003, izin laporan, kerangka laporan, export xlsx/PDF, Rekap Kas Harian, Pendapatan, Komisi Capster | Selesai |
| M4b | Jasa & Margin (bruto, K21; biaya bahan dari `treatment_line.material_cost`, estimasi resep bila NULL), Produk & Laba Kotor | Selesai |
| M4c | Stok (posisi, mutasi, pemakaian bahan), Pembatalan (dari `audit_log`/`cancelled_by`), Kunjungan Customer + penanda opsional di Registrasi (K22–K24) | Selesai |
| M4d | Reset Data Transaksi (khusus role ADMIN, R1–R5) + Restore Backup | Berikutnya |

Kerangka laporan (pakai ulang untuk laporan baru):
- Core: `Reports/ReportModels.cs` (ReportDocument/Section/Column, ReportFormat), `ReportPeriods.cs`, `ReportBuilders.cs`, data di `ReportData.cs`.
- Infrastructure: `Services/ReportService.cs` (query), `Export/XlsxWriter.cs` (xlsx tanpa NuGet).
- Presentation: `ViewModels/Reports/ReportPages.cs` — turunkan `ReportPageViewModel`, implement `BuildDocument`, panggil `Activate()`.
- Desktop: satu `ReportTemplate` + `Controls/ReportGrid.cs`; cetak A4/PDF di `Services/ReportPrinting.cs`.
- Laporan baru = data record + builder + query + VM + entri menu (izin sesuai §15) + test. Tidak perlu XAML baru.

## Rilis & installer (direncanakan SETELAH M4)
Urutan yang disepakati: **M4 → upgrade .NET 10 LTS → publish + installer**.
.NET 8 berakhir masa dukungannya 10 Nov 2026; .NET 10 LTS didukung sampai Nov 2028, jadi installer final dibuat di .NET 10.
Rencana teknis (sudah didiskusikan, belum dibuat):
- `dotnet publish` self-contained, win-x64, PublishSingleFile + IncludeNativeLibrariesForSelfExtract (wajib untuk e_sqlite3),
  ReadyToRun; JANGAN PublishTrimmed (WPF tidak mendukung). Disimpan sebagai publish profile `win-x64.pubxml`.
- Installer: **Inno Setup** (`installer/POSSalon.iss`) + `build-installer.ps1` (test → publish → ISCC); opsional GitHub Actions saat tag `v*`.
- AppId tetap (upgrade, bukan instal dobel); data di `%LOCALAPPDATA%\MSPASalon\` TIDAK disentuh installer/uninstaller;
  disarankan backup otomatis `.db` sebelum upgrade. Tanpa code signing → SmartScreen "More info → Run anyway".
Keputusan terbuka untuk rilis:
1. Self-contained (disarankan) atau framework-dependent.
2. Per-machine (Program Files) atau per-user.
3. Nama exe tetap `MSPASalon.exe` atau `POSSalon.exe` (bila diganti: alamat palet di ThemeService.cs harus dibuat relatif).
4. Build lokal saja atau + GitHub Actions.
