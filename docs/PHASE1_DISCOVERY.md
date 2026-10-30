# MSPA Salon — Fase 1: Discovery (Reverse Engineering)

**Sumber bukti:** `MspaSalon.dll` didecompile dengan ICSharpCode.Decompiler (ILSpy engine 8.2) menjadi ±7.260 baris C#; `salon.sqlite` diinspeksi pada **salinan** secara read-only; BAML dipindai untuk properti ukuran jendela. ZIP dan database asli tidak diubah.

Status audit awal: **seluruh temuan audit awal terkonfirmasi**, ditambah sejumlah defect yang lebih serius (bagian 4).

> Catatan repo publik: database lama, binary lama, dan hasil decompile **tidak** disertakan di repository ini. Dokumen ini hanya merangkum temuannya.

---

## 1. Inventaris Teknis

| Komponen | Temuan |
|---|---|
| Arsitektur | Bukan MVVM. Seluruh logika ada di code-behind Window (`TransactionEditWindow.Save_Click`, dll). CommunityToolkit.Mvvm direferensikan tetapi praktis tidak dipakai. |
| Pola modul | Generik/metadata-driven: `EntityDef` (8 master) dan `TransactionDef` (5 transaksi) memakai satu form generik. SQL dibangun dinamis dari nama tabel/kolom. |
| Akses data | `Repo` statis; **setiap query membuka koneksi baru**. Tidak ada transaksi database lintas operasi. `PRAGMA foreign_keys=OFF` dipaksa di setiap koneksi. |
| Insert | `Repo.InsertDynamic` membuang diam-diam kolom yang tidak ada di tabel. |
| Startup | `Db.Initialize()` membuat tabel + **seed data sampel** (produk, jasa, karyawan, customer, transaksi) bila tabel produk kosong. Ada menu Clear DB / Reset DB. |
| Self-test | `App` memiliki self-test internal (stok, numbering) yang menyisipkan/menghapus data `TEST01` di database produksi. |
| Keamanan | Password disimpan plaintext. Role: `SA` (super), `ADMIN`, lainnya = Kasir. |
| Ekspor/cetak | ClosedXML (Excel), QuestPDF (PDF), PrintHelper (nota). |

## 2. Inventaris Fungsional

| Menu | Isi |
|---|---|
| Master | Product, Customer, Supplier, Karyawan, Service, Category, Cabang, Users |
| Transaksi | Penjualan (FJ), Pembelian (FB), Adjustment (AD), Client Registry (RG), Client Process (PR) |
| Laporan (13) | Stok akhir, Kartu stok, History adjust, Penjualan per tanggal, Detail penjualan, Client registry, Client process, Stok akhir periode, Stock adjustment, Harga jual, Capster ready, Client service, Total pendapatan, Total komisi |
| Admin/Setting | Backup, Clear DB, Reset DB, Ganti password, Lock, Printer, Logo, Tema |
| Hak akses Kasir | Transaksi: Penjualan, Registry, Process. Hampir semua laporan. |

### Alur bisnis yang sebenarnya diimplementasikan

| Transaksi | Item | Pihak | Stok | Pembayaran |
|---|---|---|---|---|
| Penjualan | Produk (qty, harga, diskon nominal) | Customer (wajib) | −qty | Input bayar & kembalian di layar, **tidak disimpan** |
| Pembelian | Produk | Supplier (opsional) | +qty | — |
| Adjustment | Produk | — | +qty (negatif bila diketik minus) | — |
| Client Registry | Jasa + capster, tanpa qty/diskon | Customer | — | Input bayar di layar, **tidak disimpan** |
| Client Process | Produk (qty, diskon) | — (tanpa customer) | −qty | Disimpan di `process.cpay` |

**Penting:** alur "Customer → Registrasi → Treatment → Payment" **tidak ada** di aplikasi lama. Registry dan Process adalah dua dokumen berdiri sendiri dengan penomoran berbeda (`RG` vs `PR`) dan tidak ada relasi, padahal skema tabel `process` (PK `noreg`, `timein`, `timeout`, `cpay`) menunjukkan desain aslinya memang 1 registrasi = 1 process. Status selalu `'Y'`; tidak ada status treatment/selesai/lunas.

## 3. Database Lama (22 tabel)

- Tidak ada FK, tidak ada index tambahan, monetary `REAL`, detail tanpa PK, `counter` tanpa unique key.
- Stok on-hand disimpan langsung di `mproduct.stockakhir` dan di-update dengan `UPDATE ... + delta`. Tabel ledger `loginout` **tidak pernah ditulis oleh kode mana pun**, sehingga laporan Kartu Stok selalu kosong.
- `logjual` juga tidak pernah ditulis.
- `mproduct.hpp` tidak pernah diisi; HPP baris diambil dari `hargapokok` master (statis).
- Isi data: data uji coba (2 penjualan, 1 registry, 1 process, 2 adjustment, 0 pembelian) dan master data yang sudah benar.

## 4. Defect & Risiko Business Logic (terverifikasi dari kode)

| # | Severity | Temuan | Bukti | Status di aplikasi baru |
|---|---|---|---|---|
| D1 | Kritis | **Simpan transaksi tidak atomik.** Header, detail, dan update stok masing-masing membuka koneksi sendiri. | `Save_Click`, `Repo.*` | Satu transaksi `BEGIN IMMEDIATE` per aksi bisnis |
| D2 | Kritis | **Edit = hapus lalu insert ulang** di luar transaksi; dokumen bisa hilang permanen. | `DeleteDoc` | Dokumen posted tidak bisa diedit (K11) |
| D3 | Kritis | **Hapus dokumen = `DELETE` fisik**, tanpa jejak. | `DeleteDoc` | Status BATAL + alasan; nomor tetap tercatat |
| D4 | Tinggi | **Pembayaran Penjualan & Registry tidak pernah tersimpan** (kolom tidak ada, dibuang diam-diam). | `InsertDynamic` | Tabel `payment` (M2/M3) |
| D5 | Tinggi | **Underpayment tidak divalidasi**; kembalian negatif. | `RecalcKembali` | CHECK di DB (INT-07) |
| D6 | Tinggi | **Diskon tanpa batas**; total baris bisa negatif; qty/harga tidak divalidasi. | `BtnAdd_Click` | CHECK di DB + validasi |
| D7 | Tinggi | **Parsing angka merusak desimal**: `1.5` menjadi `15`. | `ParseNum` | `Quantity.TryParse` (UNIT-01) |
| D8 | Tinggi | **Nomor dokumen dikonsumsi di transaksi terpisah**, gap saat gagal; tanpa unique key. | `Numbering.Next` | UPSERT dalam transaksi dokumen (INT-03/04/05) |
| D9 | Tinggi | **Stok tidak bisa direkonsiliasi** (tanpa ledger). | Data + kode | Stock ledger + saldo awal (INT-08, INT-20) |
| D10 | Sedang | **Komisi dihitung dari master saat laporan**, bukan snapshot. | Report `komisi` | Snapshot saat posting (M2) |
| D11 | Sedang | **Registry menyimpan kode capster sebagai nama.** | `BtnAdd_Click` | Snapshot kode + nama terpisah |
| D12 | Sedang | **Total Pendapatan** menjumlah Penjualan + Registry + Process (double count). | Report `pendapatan` | Registry bukan pendapatan (§12) |
| D13 | Sedang | **Format nomor tidak konsisten** (`YYMM/FJ` vs `DDMM/PJ`). | `Numbering`, `Db.Seed` | Satu format `YYMM/KODE/0001` |
| D14 | Sedang | Tanggal transaksi selalu jam `00:00:00`. | `Save_Click` | Tanggal + jam disimpan |
| D15 | Sedang | Nilai Adjustment dihitung dengan **harga jual**. | `adjust.gtotal` | Dinilai harga pokok |
| D16 | Rendah | Stok negatif diizinkan tanpa peringatan. | `AddStock` | Penjualan ditolak; backflush diberi peringatan |
| D17 | Rendah | Self-test menulis ke DB produksi; Reset DB menanam data sampel. | `App`, `Db.Seed` | Tidak ada data sampel (INT-02) |

## 5. Penyebab Masalah Responsivitas UI (BAML)

| Jendela | Ukuran | Masalah |
|---|---|---|
| TransactionEditWindow | **1160 × 780 tetap**, panel kanan 370, entry bar `StackPanel` horizontal | Pada 1366×768 tinggi layar efektif ±728 sehingga tombol **Simpan terpotong**. Pada 1366×768 @125% (efektif 1093×614) jendela lebih besar dari layar di kedua sisi. |
| EntityEditWindow | Lebar 620, `SizeToContent`, `NoResize`, max 760 | Pada layar dengan tinggi efektif < 760, tombol bawah tidak terjangkau dan jendela tidak bisa di-resize. |
| ChangePassword / Login / Lock | `NoResize` | Dapat diterima untuk dialog kecil. |
| MainWindow | 1280×800, Maximized | Relatif aman. |

## 6. Rekomendasi: Pertahankan vs Desain Ulang

**Pertahankan:** cakupan master data; konsep komisi per jasa; laporan yang memang dipakai; peran Admin/Kasir; cetak nota; export Excel/PDF.

**Desain ulang:** seluruh layer transaksi (atomik, status eksplisit), stock ledger, tabel pembayaran, penomoran, alur registrasi → treatment → pembayaran, snapshot komisi, hashing password, penghapusan seed/self-test dari runtime, dan layout seluruh form.
