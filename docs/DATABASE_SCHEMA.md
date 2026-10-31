# MSPA Salon — Database Schema

Sumber kebenaran: `src/MSPASalon.Infrastructure/Database/Migrations/001_initial_schema.sql`.
Dokumen ini merangkum desain dan alasannya.

## 1. Konvensi

| Aspek | Konvensi | Alasan |
|---|---|---|
| Uang | `INTEGER`, Rupiah bulat | Menghilangkan error floating-point `REAL` (aplikasi lama) |
| Kuantitas | `INTEGER` × 10.000, kolom berakhiran `_x` (0,1 BTL = 1000) | 4 desimal presisi penuh (K13) |
| Tanggal | `TEXT 'yyyy-MM-dd HH:mm:ss'`, waktu lokal | Dapat diurutkan; jam tersimpan (D14) |
| Boolean | `INTEGER` 0/1 + `CHECK` | |
| Kode master | `UNIQUE COLLATE NOCASE`, disimpan huruf besar | `cst001` = `CST001` |
| Foreign key | Selalu ON (`PRAGMA foreign_keys = ON` di setiap koneksi) | Aplikasi lama mematikan FK |
| Snapshot | Baris transaksi menyimpan kode, nama, harga master | Riwayat tetap utuh walau master diubah/dihapus (K10) |
| Journal | WAL | Backup online, pembaca tidak memblokir penulis |
| Transaksi tulis | `BEGIN IMMEDIATE` | Kunci tulis diambil di awal; tidak ada operasi setengah jalan |

## 2. Tabel

### Pengaturan & keamanan

| Tabel | Isi | Kunci / constraint penting |
|---|---|---|
| `schema_version` | Riwayat migrasi | PK `version` |
| `app_setting` | Key-value (cadangan) | PK `key` |
| `branch` | Satu baris profil salon, lebar kertas nota, logo nota (`logo` BLOB, migrasi 002) | `CHECK id = 1`, `paper_width_mm IN (58,80)`; logo PNG/JPG/BMP ≤ 1 MB divalidasi di aplikasi |
| `permission` | Katalog hak akses (disinkronkan dari kode) | PK `code` |
| `role` | Role; `is_system` untuk ADMIN/KASIR | `code UNIQUE` |
| `role_permission` | Hak akses per role | PK komposit, FK CASCADE |
| `app_user` | User, hash PBKDF2, `must_change_password` | `username UNIQUE NOCASE`, FK role RESTRICT |

### Master

| Tabel | Isi | Constraint penting |
|---|---|---|
| `customer` | Customer + flag walk-in | `code UNIQUE` |
| `supplier`, `category`, `employee` | Master pendukung | `code UNIQUE`; employee `is_capster` |
| `product` | Satuan stok, isi per satuan (info), harga, min/max, flag dijual/bahan, cache stok | `CHECK is_sellable OR is_consumable`, `CHECK max = 0 OR max ≥ min`, harga ≥ 0 |
| `service` | Harga + komisi nominal | `CHECK commission ≤ price` |
| `service_material` | **Resep bahan (BOM)** per 1 customer | `UNIQUE(service_id, product_id)`, `qty_x > 0`, FK CASCADE |
| `payment_method` | Tunai / QRIS / Transfer (pencatatan saja, K8) | `code UNIQUE` |

### Penomoran

| Tabel | Isi |
|---|---|
| `doc_counter` | `(doc_type, period)` → `last_no`. Dinaikkan dengan `INSERT … ON CONFLICT DO UPDATE … RETURNING` **di dalam transaksi dokumen**, sehingga rollback tidak memakan nomor. |

### Transaksi (skema siap; layar di M2–M3)

| Tabel | Isi | Constraint penting |
|---|---|---|
| `registration` / `registration_line` | Pendaftaran: customer, jasa, capster (snapshot) | `reg_no UNIQUE`; status `MENUNGGU/DIPROSES/SELESAI/BATAL`; alasan wajib bila BATAL |
| `treatment` / `treatment_line` | Proses treatment, **1:1 dengan registrasi**; komisi snapshot | `registration_id UNIQUE`, `doc_no UNIQUE` (= nomor registrasi), `grand_total = subtotal − discount`, `discount ≤ subtotal`, LUNAS wajib `time_out` + `posted_at` |
| `sale` / `sale_line` | Penjualan produk langsung | `grand_total = subtotal − discount_total`; baris: `line_total = gross − discount`, `discount ≤ gross`, `qty_x > 0` |
| `purchase` / `purchase_line` | Pembelian | `qty_x > 0`, harga ≥ 0 |
| `stock_adjustment` / `_line` | Adjustment/opname, qty bertanda, nilai harga pokok | `qty_x ≠ 0`, alasan wajib |
| `payment` | Satu pembayaran per dokumen | `UNIQUE(source_type, source_id)`, `amount_received ≥ amount_due`, `change = received − due`, non-tunai tanpa kembalian |
| `audit_log` | **Jejak audit permanen** (migrasi 003): pembatalan, reset, restore — waktu, user, aksi, dokumen, keterangan. Tidak dihapus oleh reset data (§16) | indeks `at` |
| `stock_ledger` | **Sumber kebenaran stok** | Tepat satu dari `qty_in_x`/`qty_out_x` > 0; `UNIQUE(source_type, source_id, source_line_id, product_id)` mencegah double posting |

## 3. Relasi utama

```
customer ──< registration ──< registration_line >── service, employee
                  │ 1:1
              treatment ──< treatment_line >── service, employee
service ──< service_material >── product ──< stock_ledger
customer ──< sale ──< sale_line >── product
supplier ──< purchase ──< purchase_line >── product
role ──< role_permission >── permission ;  role ──< app_user >── employee
sale / treatment ── payment (source_type + source_id)
```

## 4. Index

Index tambahan untuk laporan dan antrian: status + tanggal registrasi/treatment, tanggal dokumen penjualan/pembelian/adjustment, `stock_ledger(product_id, moved_at)`, `stock_ledger(source_type, source_id)`, `payment(paid_at)`, serta FK yang sering di-join (product.category_id, *_line.product_id/employee_id).

## 5. Stok: ledger vs cache

* Setiap mutasi melewati `StockLedger.Post` (Infrastructure) yang menulis satu baris ledger **dan** memperbarui `product.stock_on_hand_x` dalam transaksi yang sama.
* Saldo awal dari aplikasi lama masuk sebagai baris `OPENING` (`doc_no = 'SALDO-AWAL'`).
* `StockLedger.RebuildCache` membangun ulang cache dari ledger bila diperlukan (diuji di INT-08).
* Pembatalan dokumen (K11) menghapus baris ledger dokumen tersebut dalam transaksi pembatalan.

## 6. Migrasi

* File `NNN_deskripsi.sql` di `Database/Migrations`, di-embed ke assembly.
* `DatabaseInitializer.Initialize()` dipanggil setiap start: menerapkan migrasi yang belum ada (masing-masing satu transaksi), lalu seed data referensi secara idempotent (permission, role bawaan, baris branch, metode bayar, customer walk-in, admin pertama). **Tidak pernah** menanam data sampel.
* Perubahan skema berikutnya = file migrasi baru (`004_….sql`), jangan mengubah file lama.
* **Migrasi 003** (M4a): `treatment_line.material_cost` (snapshot biaya bahan per baris jasa saat LUNAS; `NULL` = transaksi sebelum 003, laporan memakai estimasi), `cancelled_by` + `cancelled_at` di registrasi/treatment/penjualan/pembelian/adjustment, tabel `audit_log`, indeks `payment(paid_at)`, `sale(posted_at)`, `sale(customer_id)`, `treatment_line(treatment_id)`.

## 7. Lokasi & backup

* Default: `%LOCALAPPDATA%\MSPASalon\mspasalon.db` (bisa diganti dengan `--db <path>` atau env `MSPASALON_DB`).
* Backup dari menu **Backup Database** memakai SQLite Online Backup API (aman saat aplikasi berjalan) → `mspasalon_yyyyMMdd_HHmmss.db`.
* Restore manual: tutup aplikasi, salin file backup ke lokasi database dengan nama `mspasalon.db`, lalu hapus file `mspasalon.db-wal` dan `mspasalon.db-shm` lama bila ada.
