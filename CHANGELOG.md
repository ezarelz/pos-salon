# Changelog

## M4d — 2026-10-31 (.NET 8)

Alat go-live sesuai BUSINESS_RULES v0.7 §16 (R1–R5, D1–D5). Khusus role **ADMIN**. Tidak ada migrasi database baru.

- **Pengaturan → Reset Data Transaksi**: pratinjau jumlah data & rentang tanggal; peringatan keras bila ada transaksi > 14 hari;
  pilihan stok **nol** atau **stok saat ini jadi saldo awal**; password + ketik `RESET` + konfirmasi akhir; **backup otomatis wajib**
  (gagal = tidak ada yang dihapus); satu transaksi database; nomor nota kembali ke 0001; master, customer, user, pengaturan, logo, dan
  jejak audit tetap; dicatat di `audit_log`; file database dirapikan (VACUUM).
- **Pengaturan → Restore Backup**: pilih file → ringkasan isi (salon, versi, jumlah transaksi, transaksi terakhir) → password → restore.
  File divalidasi (SQLite, backup aplikasi ini, tidak dari versi lebih baru, tidak rusak). Database aktif dibackup dulu; backup versi lama
  dimigrasikan otomatis; dicatat di `audit_log`; aplikasi kembali ke layar login.
- Backup kini bisa berlabel (`…_sebelum-reset.db`, `…_sebelum-restore.db`) dan tidak pernah menimpa file lain.

**Verifikasi:** 92/92 test lolos (baru: M4-11..14, VM-17), termasuk reset & restore nyata pada database SQLite dan restore backup v1.
Tampilan WPF **belum diuji** di Windows.

## M4b + M4c — 2026-10-31 (.NET 8)

Sisa laporan BUSINESS_RULES v0.6 §14 (aturan tambahan L6–L10). Tidak ada migrasi database baru.

**Laporan baru** (menu Laporan; semua bisa Cetak / PDF / Excel)
- **Jasa & Margin** (`report.finance`): per jasa — dikerjakan, nilai bruto, komisi, biaya bahan, margin Rp & %; rekonsiliasi bruto → diskon nota → neto → margin setelah diskon. Baris lama tanpa snapshot biaya bahan diestimasi dan ditandai.
- **Produk & Laba Kotor** (`report.finance`): per produk — qty, bruto, diskon, neto, HPP (snapshot), laba kotor, margin %.
- **Laporan Stok** (`report.view`, termasuk KASIR): awal / beli / jual / treatment / adjustment / akhir per produk, nilai stok, pemakaian bahan treatment (pembanding opname); filter bahan, produk dijual, di bawah minimum.
- **Pembatalan** (`report.audit`): semua dokumen BATAL dengan waktu, user pembatal, alasan, nilai.
- **Kunjungan Customer** (`report.customer`, termasuk KASIR): status Perlu diingatkan / Tidak aktif / Aktif / Belum ada kunjungan, hari sejak kunjungan terakhir, rata-rata jarak, total belanja, jasa & capster terakhir. Default menampilkan "Perlu diingatkan".

**Fitur pendukung**
- Master Customer → tombol **Riwayat Kunjungan** (tanggal, nota, jasa, capster, total).
- Registrasi: penanda **"Terakhir datang X hari lalu · jasa dengan capster"** saat memilih customer (bisa dimatikan).
- Pengaturan Salon: batas hari Aktif / Tidak aktif (default 30 / 60) dan tombol penanda registrasi.
- Menu "Laporan Lainnya" (segera) dihapus; semua 8 laporan sudah tersedia.

**Verifikasi:** 87/87 test lolos (baru: M4-06..10, VM-16). File .xlsx (persen & nilai negatif) diverifikasi dengan openpyxl.
Tampilan WPF **belum diuji** di Windows.

## M4a — 2026-10-31 (.NET 8)

Laporan tahap pertama sesuai BUSINESS_RULES v0.5 §14–15 (keputusan K20–K25, T1–T4).

**Menu Laporan baru**
- **Rekap Kas Harian** (izin `report.view`, termasuk KASIR): per metode bayar, uang tunai yang seharusnya di laci, daftar nota + referensi QRIS; tombol **Cetak Struk Rekap** ke printer thermal (dengan kolom uang fisik & paraf).
- **Pendapatan** (`report.finance`): per hari (termasuk hari tanpa transaksi), jasa bruto/diskon/neto, produk, per metode bayar, rincian nota.
- **Komisi Capster** (`report.commission`): ringkasan & rincian per nota; pilihan cepat 1–15 / 16–akhir bulan ini & bulan lalu; memilih satu capster = **slip komisi** dengan kolom tanda tangan.
- Semua laporan: **Cetak** A4 (nomor halaman), **PDF** (dialog cetak dengan *Microsoft Print to PDF* terpilih), **Excel** (.xlsx, angka & tanggal bertipe).
- "Laporan Lainnya" (M4b/M4c) tampil sebagai "segera".

**Dashboard**: kartu capster menampilkan komisi hari ini, hanya untuk user dengan izin laporan komisi.

**Hak akses**: izin baru `report.customer`, `report.finance`, `report.commission`, `report.audit`. ADMIN otomatis memiliki semuanya.
Role KASIR yang sudah ada otomatis mendapat `report.customer` (default baru) satu kali; izin yang pernah dicabut admin tidak dikembalikan.

**Database — migrasi 003** (otomatis saat aplikasi dibuka): snapshot biaya bahan per baris jasa, `cancelled_by/cancelled_at`
di semua dokumen, tabel `audit_log` (setiap pembatalan tercatat), indeks laporan. Data lama tidak berubah.

**Verifikasi:** 81/81 test lolos (baru: M4-01..05, UNIT-17..19, VM-15, DASH-03; INT-01 kini menguji upgrade database v1 → v3).
File .xlsx diverifikasi juga dengan pembaca independen (openpyxl). Layar laporan, cetak A4/PDF, dan struk rekap **belum diuji** di Windows.

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
