# CLAUDE.md — panduan kerja untuk AI/developer

Baca file ini sebelum mengubah kode. Sumber kebenaran aturan bisnis: `docs/BUSINESS_RULES.md`.
Riwayat perubahan: `CHANGELOG.md`. Daftar test: `docs/TEST_CASES.md`.

## Status
- Selesai: M1 (master, user/role, login, import data lama, backup, shell responsif), M2 (registrasi → treatment →
  pembayaran, backflush, nota 58 mm), M3 (penjualan, pembelian, stock adjustment, kartu stok),
  M3.1 (dashboard harian, mode gelap), M3.2 (format nota `Nama x qty  harga` + logo).
- Berikutnya: **M4 Laporan** — lihat bagian "M4" di bawah. Jangan mulai tanpa konfirmasi pengguna.
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

## M4 — Laporan (rencana, belum dimulai)
Cakupan dari TECHNICAL_ARCHITECTURE §6: pendapatan, komisi, margin jasa, stok, rekap metode bayar + export Excel/PDF.
Keputusan yang sudah ada:
- Laporan Komisi Capster = **opsi B**: menu Laporan → Komisi Capster, filter periode (mis. 1–15 / 1 bulan),
  ringkasan per capster (client, jasa, nilai jasa, total komisi) + rincian per nota, bisa dicetak. Ditambah ringkasan komisi di kartu capster dashboard.
Keputusan yang MASIH TERBUKA (tanyakan sebelum membangun):
1. Diskon nota memotong komisi? (sekarang: tidak — komisi = master jasa).
2. Format export: Excel (.xlsx), PDF, atau keduanya.
3. Hak akses laporan: ADMIN saja, atau KASIR boleh melihat sebagian (mis. rekap kas harian)?
4. Laporan mana yang paling dibutuhkan lebih dulu (urutan pengerjaan).
