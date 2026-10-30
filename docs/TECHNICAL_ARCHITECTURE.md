# MSPA Salon — Technical Architecture

## 1. Solution

```
MSPASalon.sln
├── src/
│   ├── MSPASalon.Core            net8.0   Domain: entity, aturan uang/qty, validasi, permission, kontrak service
│   ├── MSPASalon.Infrastructure  net8.0   SQLite + Dapper: migrasi, service, penomoran, stock ledger, backup, import lama
│   ├── MSPASalon.Presentation    net8.0   ViewModel (CommunityToolkit.Mvvm), aturan ukuran jendela. TANPA WPF
│   └── MSPASalon.Desktop         net8.0-windows (WPF)   View XAML, converter, dialog, composition root
├── tests/MSPASalon.Tests         net8.0   Test runner konsol (unit + integrasi + ViewModel)
└── tools/MSPASalon.Import        net8.0   CLI import master data dari salon.sqlite lama
```

Arah dependensi: `Desktop → Presentation → Core` dan `Desktop → Infrastructure → Core`.
`Presentation` hanya mengenal kontrak di `Core` (`IMasterService<T>`, `IAuthService`, …), tidak mengenal SQLite.

### Mengapa ViewModel dipisah dari proyek WPF

Proyek WPF hanya bisa **dijalankan** di Windows. Dengan memindahkan seluruh ViewModel ke `MSPASalon.Presentation` (net8.0 biasa), semua logika layar (validasi input, perhitungan margin resep, filter daftar, hak akses menu, aturan ukuran jendela) bisa dites otomatis di OS apa pun, termasuk WSL dan CI. Proyek Desktop tinggal berisi XAML dan code-behind yang murni tampilan.

## 2. Pilihan teknologi

| Keputusan | Pilihan | Alasan |
|---|---|---|
| Akses data | **Dapper** (bukan EF Core) | SQL eksplisit dan mudah diaudit; kontrol penuh atas transaksi (`BEGIN IMMEDIATE`), UPSERT `RETURNING` untuk penomoran, dan constraint yang dirancang ketat. EF Core menambah lapisan (change tracker, migrasi model) yang tidak dibutuhkan untuk aplikasi single-PC sekecil ini. |
| MVVM | CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`) tanpa source generator | Ringan; properti ditulis eksplisit sehingga mudah dibaca. |
| UI library | **WPF bawaan** (tanpa WPF-UI) | Mengurangi dependensi dan risiko perubahan API; tema sendiri di `Themes/Theme.xaml`. |
| DI container | Tidak ada; composition root manual (`AppServices`, `App.xaml.cs`) | Jumlah service kecil dan statis. |
| Test framework | Runner sendiri (`tests/…/Framework/MiniTest.cs`) | Tidak perlu paket NuGet tambahan; hasil ditulis ke `tests/TestResults/test-results.md`. |
| Database | SQLite, WAL | Single PC (K5). |

## 3. Pola transaksi

Setiap aksi bisnis = satu koneksi + satu transaksi `BEGIN IMMEDIATE`:

```
using var c  = Db.Open();                          // foreign_keys = ON, busy_timeout
using var tx = DbConnectionFactory.BeginWrite(c);  // kunci tulis di awal
… validasi berbasis DB → insert/update header → detail → ledger → payment → status …
tx.Commit();                                       // exception di mana pun → rollback otomatis (Dispose)
```

* `DocumentNumberGenerator.Next(c, tx, …)` memakai **transaksi yang sama**, jadi nomor hanya terpakai jika dokumen tersimpan.
* `StockLedger.Post(c, tx, …)` menulis ledger dan cache stok pada transaksi yang sama.
* Pelanggaran aturan bisnis → `BusinessException` (pesan Bahasa Indonesia untuk pengguna). Error lain → teknis, dicatat ke `%LOCALAPPDATA%\MSPASalon\logs\error-yyyyMMdd.log`.

## 4. Responsivitas UI

| Masalah lama | Solusi |
|---|---|
| Jendela ukuran tetap 1160×780, `NoResize` | Ukuran awal = min(ukuran ideal, 92% work area); `MinWidth/MinHeight` tidak pernah melebihi layar (`WindowSizing`, dites di UNIT-09). Main window di-maximize pada layar kecil. |
| Tombol Simpan terpotong | Semua editor memakai struktur **judul (Auto) · konten dalam ScrollViewer (\*) · footer tombol (Auto)**; footer tidak pernah ikut tergulir. |
| Entry bar `StackPanel` horizontal | `WrapPanel` untuk toolbar; `FormPanel` (panel kustom) untuk form: 2 kolom bila lebar, 1 kolom bila sempit, field melebar mengikuti jendela. |
| Navigasi memakan ruang | Lebar 232 DIP, otomatis menjadi ikon saja (56 DIP) di bawah 1000 DIP; aksi akun tetap di dasar nav. |
| Kolom grid lebar tetap | Satu kolom bintang (\*) mengisi sisa lebar, kolom lain `Auto` dengan `MinWidth`. |
| Skala DPI | Manifest `PerMonitorV2`; seluruh ukuran dalam DIP. |

Target uji: 1280×720, 1366×768, 1920×1080 × skala 100/125/150%. Kombinasi terkecil: **1280×720 @150% ≈ 853×440 DIP**; desain minimum 800×440. Checklist: `docs/UAT_RESPONSIVE_CHECKLIST.md`.

## 5. Keamanan

* Password: PBKDF2-HMAC-SHA256, 100.000 iterasi, salt 16 byte per user.
* Admin awal `admin/admin` dan user hasil import **wajib ganti password** pada login pertama.
* Hak akses: tabel `permission` (katalog dari kode) + `role_permission`. Role baru bisa dibuat dari layar Role & Hak Akses. ADMIN selalu memiliki semua hak akses.
* Proteksi: admin aktif terakhir, customer walk-in terakhir, dan metode Tunai terakhir tidak bisa dihapus/dinonaktifkan.

## 6. Status milestone

| Milestone | Isi | Status |
|---|---|---|
| **M1** | Solution, skema lengkap, migrasi, penomoran, stock ledger, master data + resep jasa, user/role, login, backup, import master, shell WPF responsif, dashboard | **Selesai** (non-WPF: build + 38 test lolos; WPF: perlu build di Windows) |
| M2 | Registrasi → Proses Treatment (antrian, mulai, ubah jasa/capster, selesai & bayar, backflush, batal), cetak nota 58 mm | Selesai |
| M3 | Penjualan produk, pembelian, stock adjustment, kartu stok | Selesai |
| M4 | Laporan (pendapatan, komisi, margin jasa, stok, rekap metode bayar) + export Excel/PDF | Berikutnya |
