# MSPA Salon — Business Rules (v0.4)

Dokumen ini adalah **sumber kebenaran** untuk aplikasi baru. Aplikasi lama hanya referensi.
Penanda: **[KEPUTUSAN]** = sudah disepakati pengguna · **[ASUMSI]** = default sementara, perlu konfirmasi, diisolasi agar mudah diubah.

---

## 0. Log Keputusan

| ID | Keputusan | Tanggal |
|---|---|---|
| K1 | Registry dan Process **tetap dua tahap terpisah**, tetapi Process **wajib terhubung** ke Registry: registrasi yang tersimpan langsung tersedia di antrian Process. | 2026-10-31 |
| K2 | Pembayaran **hanya di tahap Process**. Pengecualian: Penjualan Produk langsung memiliki pembayaran sendiri. | 2026-10-31 |
| K3 | Process **tidak memiliki baris produk yang ditagih**. Bahan habis pakai (mis. shampo) dikurangi otomatis dari stok melalui **backflush** berdasarkan resep jasa. | 2026-10-31 |
| K4 | Jasa dan capster **boleh ditambah/diganti** selama treatment berjalan. | 2026-10-31 |
| K5 | Operasional **single PC**, sehingga SQLite lokal cukup. | 2026-10-31 |
| K6 | Prioritas pertama: **seluruh jendela responsif**. Build harus bisa dari Windows maupun WSL (`EnableWindowsTargeting=true`). | 2026-10-31 |
| K7 | Kode dikirim sebagai ZIP, lalu dipindah ke repo GitHub (boleh publik). | 2026-10-31 |
| K8 | Metode pembayaran **hanya pencatatan** (Tunai, QRIS, Transfer, dst.). Tidak ada integrasi atau QR/barcode; QRIS diproses di sistem terpisah. | 2026-10-31 |
| K10 | Aplikasi untuk **pemakaian sendiri**: hapus master **diperbolehkan**, tanpa mekanisme reversal yang kompleks. | 2026-10-31 |
| K11 | **Pembatalan sederhana**: tidak ada dokumen void terpisah. *Batal* = status dokumen menjadi BATAL + baris stok dan pembayarannya dihapus dalam satu transaksi database. Nomor dokumen tetap tercatat dan tidak dipakai ulang. | 2026-10-31 |
| K12 | Hapus produk yang punya riwayat stok **diperbolehkan** dengan peringatan; kartu stok produk itu ikut terhapus, riwayat transaksi tetap terbaca dari snapshot. | 2026-10-31 |
| K13 | Presisi kuantitas **4 desimal** (terkecil 0,0001). | 2026-10-31 |
| K14 | Role bawaan **ADMIN** (= SA lama) dan **KASIR**; role baru bisa ditambah kapan saja dengan hak akses pilihan. | 2026-10-31 |
| K15 | Cetak nota ke **printer thermal 58 mm** (80 mm sebagai opsi), dibutuhkan di M2. | 2026-10-31 |
| K19 | Format nota: logo (opsional) di tengah → nama salon → alamat → telp. Baris item `Nama x qty  harga`; **nama capster tidak dicetak**; jasa yang sama (nama & harga sama) dalam satu nota digabung jadi satu baris dengan qty. Berlaku juga untuk penjualan produk. | 2026-10-31 |
| K16 | **Import master data** dari database lama (bukan transaksi); data awal dianggap sudah benar. | 2026-10-31 |
| K17 | Export laporan Excel/PDF tetap dibutuhkan (M4). | 2026-10-31 |
| K18 | Asumsi A2, A3, A4, A6, A7 disetujui sebagai default (tidak dikoreksi pengguna). | 2026-10-31 |
| K9 | Resep bahan diisi sebagai **pemakaian per 1 customer/jasa dalam satuan stok desimal**, mis. Cuci Rambut memakai Shampo **0,100 BTL**. | 2026-10-31 |

---

## 1. Aturan Global

| Aturan | Ketentuan |
|---|---|
| Uang | Rupiah bulat (tanpa sen). Disimpan sebagai INTEGER, dihitung dengan `decimal`. Tidak ada PPN/PB1 **[ASUMSI A1]**. |
| Kuantitas | Desimal **4 digit** (mis. 0,1 BTL; terkecil 0,0001) **[K13]**. Disimpan sebagai INTEGER × 10.000, sehingga tidak ada error floating-point. Input menerima koma maupun titik sebagai pemisah desimal, tanpa pemisah ribuan. |
| Pembulatan | Karena semua input harga, diskon, dan komisi berupa Rupiah bulat, tidak ada pembulatan di transaksi. Input qty lebih dari 4 desimal ditolak (tidak dibulatkan diam-diam). Nilai biaya bahan (qty × harga pokok) dibulatkan ke Rupiah terdekat per baris. |
| Siklus dokumen | Setiap dokumen memiliki status eksplisit. Dokumen yang sudah **diposting tidak bisa diedit atau dihapus**, hanya bisa **dibatalkan** dengan alasan (hak akses khusus) **[K11]**: status menjadi BATAL dan efek stok/pembayarannya dihapus dalam satu transaksi. |
| Atomik | Satu aksi bisnis (simpan, posting, batal) = satu transaksi database. Gagal di titik mana pun berarti seluruhnya batal. |
| Penomoran | Format lama dipertahankan: `YYMM/KODE/0001`, reset bulanan per kode. Nomor diambil **di dalam transaksi yang sama** dengan penyimpanan dokumen, sehingga tidak ada gap akibat gagal simpan. Database menjamin UNIQUE. |
| Tanggal | Disimpan lengkap dengan jam (waktu lokal). |
| Hapus master | Diperbolehkan **[K10]**. Transaksi menyimpan snapshot kode, nama, dan harga, sehingga riwayat tetap terbaca setelah master dihapus. |

## 2. Master Data

| Master | Atribut kunci & aturan |
|---|---|
| Customer | Kode, nama, HP, jenis kelamin, member, aktif. Tersedia customer default **"UMUM / Walk-in"**. |
| Produk | Kode, nama, kategori, **satuan stok** (mis. BTL), harga pokok, harga jual, stok min/max, flag *dijual* / *bahan habis pakai* (boleh keduanya). Opsional: **isi per satuan** (mis. 1 BTL = 200 ml), hanya sebagai informasi bantu. |
| Jasa | Kode, nama, harga, **komisi nominal per jasa**, **resep bahan (BOM)**. |
| Resep Jasa (BOM) | Per jasa: daftar bahan + **qty pemakaian per 1 customer dalam satuan stok, desimal hingga 4 digit**. Contoh: Cuci Rambut = Shampo **0,100 BTL**; Creambath = Shampo 0,100 BTL + Masker Rambut 0,150 BTL. Qty > 0; produk harus berflag *bahan habis pakai*; satu produk maksimal sekali per resep. Bila *isi per satuan* diisi, layar menampilkan ekuivalennya (0,100 BTL × 200 ml = 20 ml) sebagai bantuan. |
| Karyawan | Kode, nama, jabatan, flag capster, aktif. |
| User | Username, karyawan terkait, role (**SA / Admin / Kasir**, hak akses sama seperti aplikasi lama), password ter-hash. |
| Supplier, Kategori, Cabang | Seperti aplikasi lama. |

## 3. Registry (Pendaftaran Customer)

**Proses bisnis:** customer datang → resepsionis memilih customer, jasa, dan capster per jasa → Simpan → registrasi masuk **antrian Process**.

| Aturan | Ketentuan |
|---|---|
| R1 | Wajib: customer dan minimal 1 baris jasa. Capster wajib per baris jasa dan harus karyawan aktif berflag capster. |
| R2 | Harga jasa diambil dari master saat dipilih (snapshot). **Tidak ada diskon dan pembayaran** di registrasi. |
| R3 | Status setelah Simpan: **MENUNGGU**. |
| R4 | Selama MENUNGGU, registrasi masih boleh diedit atau dibatalkan (alasan wajib). |
| R5 | Setelah Process dimulai, registrasi **terkunci**. Perubahan jasa/capster dilakukan di layar Process (K4). |
| R6 | Registrasi tidak memengaruhi stok, pendapatan, maupun komisi. |

## 4. Process (Treatment & Pembayaran)

**Proses bisnis:** kasir memilih registrasi dari antrian → **Mulai** (jam masuk tercatat) → treatment berjalan (jasa/capster boleh berubah) → **Selesai & Bayar** → posting.

| Aturan | Ketentuan |
|---|---|
| P1 | Process **hanya bisa dibuat dari registrasi** (relasi wajib) dan **satu registrasi = satu process** (UNIQUE). Nomor process = nomor registrasi. |
| P2 | Saat Mulai: baris jasa + capster disalin dari registrasi, status menjadi **DIKERJAKAN**, `time_in` = saat ini. |
| P3 | Selama DIKERJAKAN: boleh tambah, hapus, atau ganti jasa dan capster. Minimal 1 jasa tersisa saat posting. Jasa yang **sudah ada** mempertahankan harga snapshot-nya; jasa **baru/diganti** memakai harga master saat itu (v0.4). |
| P4 | Total = Σ harga jasa − diskon. Diskon berupa **nominal di level header**, 0 ≤ diskon ≤ subtotal **[ASUMSI A2]**. |
| P5 | Posting (Selesai & Bayar), dalam **satu transaksi database**: `time_out` & durasi; snapshot komisi per baris (capster final); backflush bahan (bagian 5); pencatatan pembayaran (bagian 9); status **LUNAS**; status registrasi **SELESAI**. |
| P6 | Pembayaran wajib lunas saat posting; tidak ada piutang **[ASUMSI A3]**. |
| P7 | Process yang belum diposting boleh dibatalkan (alasan wajib). Registrasi ikut berstatus BATAL. Tidak ada efek stok. |
| P8 | Process yang sudah LUNAS hanya bisa **dibatalkan** oleh user dengan hak *Batalkan Dokumen yang Sudah Diposting* (alasan wajib): status BATAL, baris backflush dan pembayaran dihapus, komisi tidak dihitung **[K11]**. |

**Status:**
```
Registry : MENUNGGU → DIPROSES → SELESAI
               └→ BATAL           └→ BATAL (bila process dibatalkan sebelum posting)
Process  : DIKERJAKAN → LUNAS → BATAL
               └→ BATAL
```

## 5. Backflush Bahan Habis Pakai

| Aturan | Ketentuan |
|---|---|
| B1 | Dijalankan **otomatis saat posting Process**, berdasarkan jasa **final** (setelah perubahan selama treatment). |
| B2 | Qty backflush = Σ (qty resep per jasa × jumlah baris jasa tersebut) per produk. Contoh: process berisi Cuci Rambut (Shampo 0,100) + Creambath (Shampo 0,100; Masker 0,150), maka stok Shampo berkurang **0,200 BTL** dan Masker **0,150 BTL**. Tanpa konversi satuan, karena resep sudah dalam satuan stok. |
| B3 | Jasa tanpa BOM tidak mengurangi stok (tidak error). |
| B4 | Backflush **tidak boleh menggagalkan** penyelesaian treatment yang sudah dikerjakan. Bila stok tidak cukup, stok boleh negatif dan transaksi diberi tanda peringatan di dashboard **[ASUMSI A4]**. |
| B5 | Qty yang di-backflush adalah **standar**. Selisih pemakaian aktual dikoreksi melalui **Stock Opname/Adjustment** berkala. Qty tidak bisa diubah per transaksi **[ASUMSI A5]**. |
| B6 | Setiap baris backflush tercatat di stock ledger dengan referensi process + jasa + produk, dibatasi UNIQUE agar tidak bisa terposting ganda. |
| B7 | Nilai pemakaian = qty × harga pokok produk saat posting (snapshot) **[ASUMSI A6: standard cost, bukan moving average]**. |

## 6. Penjualan Produk Langsung

| Aturan | Ketentuan |
|---|---|
| S1 | Customer wajib (default "UMUM / Walk-in"). Hanya produk berflag *dijual*. |
| S2 | Per baris: qty > 0, harga ≥ 0, **diskon nominal per baris** 0 ≤ diskon ≤ qty × harga. Total baris = qty × harga − diskon. |
| S3 | Grand total = Σ total baris; header harus sama dengan Σ detail (divalidasi sistem dan database). |
| S4 | Penjualan langsung **diposting saat dibayar** ("Bayar & Simpan"): stok berkurang + pembayaran tercatat + status POSTED, dalam satu transaksi. Tidak ada DRAFT di M3 (penyederhanaan v0.4: penjualan kasir selalu dibayar di tempat). |
| S5 | Stok tidak cukup akan **menolak posting** **[ASUMSI A7]**. |
| S6 | Pembayaran wajib lunas (bagian 9). Pembatalan setelah posting menghapus baris stok dan pembayarannya **[K11]**. |

## 7. Pembelian

Supplier wajib. Posting menambah stok dan mencatat harga beli per baris. Harga pokok master **tidak** diperbarui otomatis **[ASUMSI A8]**. Pembatalan menghapus baris stoknya, dan ditolak bila akan membuat stok negatif.

## 8. Stock Adjustment / Opname

Qty bertanda (+/−), alasan wajib, dinilai dengan **harga pokok** (bukan harga jual seperti aplikasi lama). Posting membuat baris ledger; pembatalan menghapusnya. Adjustment yang membuat stok produk **negatif ditolak** (v0.4); begitu pula pembatalan adjustment (+) bila barangnya sudah terpakai.

## 9. Pembayaran

| Aturan | Ketentuan |
|---|---|
| Y1 | Metode pembayaran adalah **daftar master sederhana**: Tunai, QRIS, Transfer (bisa ditambah, mis. Debit). Fungsinya **pencatatan saja**, tanpa integrasi, QR, atau barcode **[K8]**. |
| Y2 | Satu metode per transaksi. |
| Y3 | **Tunai:** bayar ≥ total, kembalian = bayar − total. Kurang bayar → posting ditolak. |
| Y4 | **Non-tunai:** jumlah otomatis = total (tidak ada kembalian). Nomor referensi **opsional** (mis. 4 digit terakhir atau ID transaksi QRIS) untuk mencocokkan dengan sistem QRIS terpisah. |
| Y5 | Pembayaran disimpan di tabel tersendiri (metode, total, diterima, kembalian, referensi), sehingga laporan harian bisa menampilkan **rekap per metode** (Tunai vs QRIS vs Transfer) untuk pencocokan kas dan mutasi. |

## 10. Stock Ledger

Stok on-hand = Σ ledger (masuk − keluar). Sumber: Pembelian (+), Penjualan (−), Backflush Process (−), Adjustment (±), Pembatalan dokumen menghapus baris ledger dokumen itu **[K11]**. Kolom saldo di master hanya **cache** yang diperbarui dalam transaksi yang sama dan dapat direkonsiliasi ulang dari ledger.

## 11. Komisi

Komisi = nominal komisi jasa (snapshot saat posting Process) yang dikreditkan ke **capster final** baris tersebut. Process BATAL tidak menghasilkan komisi. Laporan komisi hanya membaca snapshot, bukan master.

## 12. Implikasi Akuntansi (referensi; aplikasi ini bukan GL)

| Kejadian | Jurnal ekuivalen |
|---|---|
| Penjualan produk diposting | Dr Kas/Bank · Cr Pendapatan Produk; Dr HPP Produk · Cr Persediaan |
| Process diposting | Dr Kas/Bank · Cr Pendapatan Jasa (neto diskon); Dr **Biaya Bahan Jasa** · Cr Persediaan (backflush) |
| Komisi | Dr Beban Komisi · Cr Utang Komisi Karyawan |
| Pembelian | Dr Persediaan · Cr Kas/Utang Usaha |
| Adjustment | Dr/Cr Selisih Persediaan · Cr/Dr Persediaan |
| Batal | Jurnal dokumen asal dianggap tidak pernah terjadi (dihapus dari laporan) |

Laporan **Total Pendapatan** = Penjualan POSTED + Process LUNAS. Registry **bukan** pendapatan (memperbaiki double-count di aplikasi lama).

## 13a. Dashboard (definisi angka)

| Angka | Definisi |
|---|---|
| Pendapatan hari ini | Σ grand total treatment **LUNAS** + penjualan **POSTED** dengan waktu posting hari ini (setelah diskon). Dokumen BATAL tidak dihitung. |
| Client selesai | Jumlah treatment LUNAS hari ini. Rata-rata = pendapatan jasa ÷ client selesai. |
| Antrian | Kondisi saat ini: registrasi MENUNGGU + treatment DIKERJAKAN (tidak dibatasi tanggal). |
| Rekap pembayaran | Per metode, Σ jumlah tagihan (bukan uang diterima), sehingga total rekap = pendapatan dan baris tunai = uang yang seharusnya ada di laci. |
| Kinerja capster | Per capster: jumlah client, jumlah jasa, nilai jasa **sebelum** diskon nota (diskon ada di level header, P4). |

## 13. Asumsi (✔ = disetujui sebagai default, K18)

| ID | Asumsi default | Dampak bila berubah |
|---|---|---|
| A1 | Tanpa pajak | Tambah komponen pajak di total |
| A2 ✔ | Diskon Process nominal di header | Pindah ke level baris |
| A3 ✔ | Process wajib lunas, tanpa piutang | Tambah status piutang & pelunasan |
| A4 ✔ | Backflush boleh membuat stok negatif (dengan peringatan) | Blokir posting saat stok kurang |
| A5 | Qty backflush standar, tidak bisa diubah per transaksi | Tambah layar koreksi bahan per process |
| A6 ✔ | Biaya = harga pokok master (standard cost) | Moving average dari pembelian |
| A7 ✔ | Penjualan langsung ditolak bila stok kurang | Izinkan negatif |
| A8 | Pembelian tidak mengubah harga pokok master | Update otomatis / moving average |
