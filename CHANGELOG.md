# Changelog

## M3.2 — 2026-10-31 (.NET 8)

**Format nota baru (K19)**
- Header: **logo** (opsional, di tengah) → nama salon → alamat → no. telp.
- Baris item: `Barber x 1            60.000`; nama capster tidak lagi dicetak; jasa yang sama digabung (`Cuci Rambut x 2`).
  Nama yang terlalu panjang dibungkus, harga tetap rata kanan di baris terakhirnya. Format yang sama untuk nota penjualan (diskon baris tetap di bawah item).
- Logo diatur di **Pengaturan → Pengaturan Salon → Pilih Logo…** (PNG/JPG/BMP, maks. 1 MB), dengan pratinjau; langsung tersimpan.

**Database**: migrasi `002_branch_logo.sql` (kolom `branch.logo`). Diterapkan otomatis saat aplikasi dibuka; data lama tidak berubah.
Logo disimpan di database sehingga ikut ter-backup.

**Verifikasi:** 71/71 test lolos (baru: UNIT-16, INT-LOGO-01, VM-14; INT-01 kini juga menguji upgrade v1 → v2).
Cetak logo ke printer thermal **belum diuji** di Windows.

## M3.1 — 2026-10-31 (.NET 8)

**Dashboard harian** (menggantikan jumlah master data & daftar "jasa tanpa resep"):
- Kartu KPI hari ini: Pendapatan (jasa + produk, dibanding kemarin), Client selesai (rata-rata per client), Antrian saat ini (menunggu · dikerjakan), Transaksi (treatment · penjualan · batal).
- Transaksi terakhir hari ini, Rekap pembayaran per metode (termasuk uang tunai yang seharusnya ada di laci), Kinerja capster, Stok di bawah minimum.
- Tombol cepat ke Registrasi Customer, Proses Treatment, Penjualan Produk; tombol muat ulang.

**Mode gelap**: tombol "Mode Gelap / Mode Terang" di bagian bawah menu. Pilihan tersimpan di
`%LOCALAPPDATA%\MSPASalon\ui-settings.json` (per komputer, bukan di database) dan dipakai lagi saat aplikasi dibuka, termasuk di layar login.
Title bar Windows 10/11 ikut gelap.

**Teknis**: warna dipindah ke `Themes/Palette.Light.xaml` / `Palette.Dark.xaml` (kunci identik, kontras teks ≥ 4,5:1) dan
dirujuk dengan `DynamicResource`. Template baru untuk ComboBox, TabItem, ListBoxItem, ScrollBar (template bawaan Windows
memakai warna terang yang tidak bisa diubah). Popup kalender DatePicker tetap bergaya terang di kedua mode.

**Verifikasi:** 68/68 test lolos (2 baru: DASH-01, DASH-02). Tampilan WPF **belum diuji** di Windows.

## M3 — 2026-10-31 (.NET 8)

M2 (Registrasi → Treatment → Pembayaran) dan M3 (Penjualan, Pembelian, Stock Adjustment, Kartu Stok).
Database M1/M1.1 tetap dipakai; tidak ada perubahan skema.

**Menu baru**
- Transaksi → **Registrasi Customer**: customer, jasa + capster per baris, total; setelah simpan ditawarkan "Proses treatment sekarang?". Ubah/batal hanya saat MENUNGGU.
- Transaksi → **Proses Treatment**: antrian di kiri; Mulai Treatment; tambah/ganti/hapus jasa & capster; pratinjau bahan backflush beserta stok sebelum → sesudah (merah bila kurang); diskon header; pembayaran Tunai/QRIS/Transfer dengan kembalian langsung; Selesai & Bayar → tawaran cetak nota 58 mm; Batalkan.
- Transaksi → **Riwayat Treatment** (filter tanggal, lihat/cetak ulang, batalkan LUNAS dengan hak akses) dan **Penjualan Produk** (Bayar & Simpan, cetak nota, batal).
- Inventori → **Pembelian**, **Stock Adjustment**, **Kartu Stok** (saldo awal, masuk, keluar, saldo akhir per periode).

**Aturan** (BUSINESS_RULES v0.4): penjualan langsung diposting saat dibayar (tanpa draft); jasa lama di treatment tetap harga snapshot, jasa baru harga master; adjustment yang membuat stok negatif ditolak.

**Verifikasi:** 66/66 test lolos (SDK 8.0.133, Linux) termasuk rollback atomik treatment & penjualan.
Layar WPF baru **belum diuji** di Windows; cek statis XAML (resource, converter `Conv.*`, binding ke properti ViewModel) lolos.

## M1.1 — 2026-10-31 (.NET 8)

Perbaikan dari uji pertama di Windows. Tidak ada perubahan business logic maupun struktur database;
database yang sudah ada di `%LOCALAPPDATA%\MSPASalon\mspasalon.db` tetap dipakai apa adanya.

| # | Gejala | Penyebab | File |
|---|---|---|---|
| 1 | Login `admin/admin` selalu "Username atau password salah"; layar Ganti Password juga terdampak | Attached property `BoundPassword` ber-default `""` sama dengan nilai awal ViewModel, sehingga handler `PasswordChanged` tidak pernah terpasang dan password yang diketik tidak sampai ke ViewModel | `src/MSPASalon.Desktop/Controls/PasswordBoxAssistant.cs` |
| 2 | Setelah login: `XamlParseException: Cannot find resource named 'ZeroToVisible'` (dashboard); form editor juga berisiko | Converter dideklarasikan sebagai resource di `App.xaml` dan tidak terjangkau dari DataTemplate di merged dictionary. **Perbaikan final:** converter tidak lagi berupa resource; XAML merujuk instance statis `Conv.*` lewat `{x:Static}` (di-resolve per tipe, tidak bisa gagal 'Cannot find resource') | `Converters/Converters.cs` (kelas `Conv`), `App.xaml`, `Views/MainWindow.xaml`, `Views/LoginWindow.xaml`, `Views/EditorWindow.xaml`, `Views/PageTemplates.xaml`, `Views/Editors/EditorTemplates.xaml` |
| 3 | (Pencegahan) potensi `InvalidOperationException` saat menekan Batal/Esc di editor | `DialogResult` bisa di-set dua kali bila WPF juga menangani `IsCancel` | `src/MSPASalon.Desktop/Views/EditorWindow.xaml.cs` |

Verifikasi: build non-WPF + 38/38 test lolos di SDK 8.0.133; pengecekan statis XAML kini memeriksa
bahwa setiap `StaticResource` terjangkau sesuai urutan merged dictionary. Tampilan WPF diuji di Windows oleh pengguna.

## M1 — 2026-10-31 (.NET 8)

Rilis awal: master data + resep jasa, user & role, login, backup, import data lama, shell responsif.
