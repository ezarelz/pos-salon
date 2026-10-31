# POS Salon — Test Cases (M1–M4d)

Jalankan: `dotnet run --project tests/MSPASalon.Tests` (filter: tambahkan `-- INT-` / `-- UNIT-` / `-- VM-`).
Hasil aktual terakhir ditulis otomatis ke `tests/TestResults/test-results.md`.

**Hasil eksekusi M4d (Linux, .NET 8.0.133): 92 / 92 PASS** (M1: 38, M2/M3: 28, dashboard: 3, nota: 3, M4: 21).

## Unit — aturan domain

| ID | Input | Expected | Hasil |
|---|---|---|---|
| UNIT-01 | Qty `"0,1"`, `"0.1"`, `"1.5"`, `"1.000,5"` | 0,1 · 0,1 · **1,5** (bukan 15, D7) · ditolak | PASS |
| UNIT-02 | Qty `0,0001`, `0,00001`; 3 × 0,1 | Tersimpan 1; ditolak (>4 desimal); tepat 0,3 | PASS |
| UNIT-03 | Uang `"28.000"`, `"Rp 1.250.000"`, `"28.000,50"` | 28000 · 1250000 · ditolak (sen) | PASS |
| UNIT-04 | 0,3333 × 3.000; 0,1 × 20.000 | 1.000 (half away from zero) · 2.000 | PASS |
| UNIT-05 | RG, 31-10-2026, seq 1 | `2610/RG/0001` | PASS |
| UNIT-06 | Produk tanpa flag; min 10 > max 5; isi tanpa satuan | Masing-masing ditolak dengan pesan | PASS |
| UNIT-07 | Jasa komisi 50.000 > harga 40.000; bahan duplikat; qty 0 | 3 pesan error | PASS |
| UNIT-08 | Hash `"rahasia1"`; verifikasi benar/salah | Benar = true; salah = false; salt unik | PASS |
| UNIT-09 | Ukuran ideal 1160×780 di area 853×440 … 1920×1040 | Jendela & minimum selalu ≤ area layar | PASS |
| UNIT-10 | Lebar 853 / 999 / 1000 | Ringkas · ringkas · normal | PASS |

## Integrasi — database (SQLite nyata, file sementara)

| ID | Input | Expected | Hasil |
|---|---|---|---|
| INT-01 | Database baru; inisialisasi 2× | Versi skema 1; `foreign_keys = 1`; WAL; integrity ok | PASS |
| INT-02 | Database baru | Role ADMIN/KASIR; admin wajib ganti pwd; TUNAI/QRIS/TRANSFER; walk-in; **0 produk/penjualan sampel** | PASS |
| INT-03 | Nomor RG 1-Okt, 31-Okt, 1-Nov; FJ 5-Okt | `2610/RG/0001`, `0002`, `2611/RG/0001`, `2610/FJ/0001` | PASS |
| INT-04 | Ambil nomor lalu rollback; ambil lagi | Nomor sama (tanpa gap, D8) | PASS |
| INT-05 | Insert `2610/RG/0001` dua kali | Kedua ditolak UNIQUE | PASS |
| INT-06 | Baris registrasi ke header id 999 | Ditolak FOREIGN KEY | PASS |
| INT-07 | Bayar 20.000 utk 28.000; QRIS dgn kembalian; kembalian salah | Ketiganya ditolak CHECK; tunai 30.000/kembali 2.000 & QRIS pas diterima | PASS |
| INT-08 | Saldo awal 5; backflush 0,1 + 0,2; posting ulang baris sama; cache dirusak | Saldo 4,7 = cache; double posting ditolak; cache dibangun ulang = 4,7 | PASS |
| INT-09 | Backup online | File backup utuh, berisi data | PASS |
| INT-10 | Customer `" cst001 "`; kode `Cst001` lagi | Tersimpan `CST001`; duplikat ditolak; saran kode `CST002` | PASS |
| INT-11 | Produk min 0,0125, max 10,5, harga 12.000 | Terbaca persis; `typeof` kolom = integer | PASS |
| INT-12 | Resep Shampo 0,1; resep dgn produk non-bahan | Tersimpan; non-bahan ditolak & header ikut rollback | PASS |
| INT-13 | Lepas flag bahan pada produk yg dipakai resep | Ditolak, menyebut nama jasa | PASS |
| INT-14 | Hapus kategori & produk yang dipakai | Peringatan; kategori produk jadi kosong; baris resep ikut terhapus | PASS |
| INT-15 | Hapus/nonaktifkan walk-in & Tunai terakhir | Ditolak; QRIS boleh dihapus | PASS |
| INT-16 | Login benar, salah, user nonaktif | Session · "salah" · "tidak aktif" | PASS |
| INT-17 | Ganti pwd dgn pwd lama salah / terlalu pendek / valid | Ditolak · ditolak · berhasil, flag wajib-ganti hilang | PASS |
| INT-18 | KASIR; role baru GUDANG (produk + pembelian) | KASIR tak bisa master produk; GUDANG sesuai centangan; role dipakai tak bisa dihapus | PASS |
| INT-19 | Nonaktifkan/ganti role admin terakhir; hapus diri sendiri | Semua ditolak | PASS |
| INT-20 | Import DB lama (skema asli, data fiktif) | Jumlah sesuai; stok 41 via ledger; REAL dibulatkan; walk-in lama dilebur; pwd ter-hash; **file lama tidak berubah**; import ulang ditolak | PASS |
| INT-21 | Import file bukan DB MSPA / file tidak ada | Ditolak, database tujuan tetap kosong | PASS |

## ViewModel (tanpa WPF)

| ID | Input | Expected | Hasil |
|---|---|---|---|
| VM-01 | Editor produk: harga `"Rp 28.000"`, min `"0,5"`; lalu `"18.000,50"`/`"abc"` | Tersimpan benar; input salah → ErrorText, tidak tersimpan | PASS |
| VM-02 | Editor jasa: Shampo 0,1 (200 ml, 20.000) + Masker 0,15 (30.000), harga 65.000, komisi 20.000 | "≈ 20 ml"; biaya 6.500; margin 38.500; duplikat ditolak; resep tersimpan | PASS |
| VM-03 | Daftar customer: cari "budi 0811"; hapus batal/ya; hapus walk-in | 1 dari 3; batal tidak menghapus; walk-in → pesan error | PASS |
| VM-04 | Shell sebagai KASIR; buka tab 2×; lebar 853 → 1366 | Menu terbatas; tab tidak dobel; Laporan (M4) "segera"; nav ringkas → normal | PASS |
| VM-05 | Login salah lalu benar | Pesan error, pwd dikosongkan; event LoggedIn | PASS |
| VM-06 | Editor role ADMIN; role baru SUPERVISOR | ADMIN semua tercentang & terkunci; SUPERVISOR tersimpan sesuai centangan | PASS |
| VM-07 | User baru, konfirmasi pwd beda lalu sama | Ditolak lalu tersimpan; role default KASIR; wajib ganti pwd | PASS |

## Unit — aturan transaksi (M2/M3)

| ID | Skenario | Hasil |
|---|---|---|
| UNIT-11 | Baris penjualan: gross = round(qty x harga), diskon <= gross, qty/harga valid | PASS |
| UNIT-12 | Total treatment: diskon header 0..subtotal (P4) | PASS |
| UNIT-13 | Pembayaran: tunai pas/lebih/kurang; non-tunai selalu = total tanpa kembalian (K8) | PASS |
| UNIT-14 | Backflush: konsumsi = jumlah resep semua baris jasa; jasa dobel dihitung dua kali; tanpa resep = 0 | PASS |
| UNIT-15 | Nota thermal: lebar 32 (58 mm) / 48 (80 mm), total & kembalian tercetak, status batal | PASS |

## Integrasi — Registrasi → Treatment → Pembayaran (M2, database SQLite sungguhan)

| ID | Skenario | Hasil |
|---|---|---|
| M2-01 | Registrasi: nomor RG, status MENUNGGU, snapshot harga & capster; input salah ditolak tanpa memakan nomor | PASS |
| M2-02 | Registrasi: ubah & batal hanya saat MENUNGGU; alasan wajib | PASS |
| M2-03 | Mulai treatment: nomor = nomor registrasi, jasa tersalin, status DIPROSES, tidak bisa dimulai dua kali | PASS |
| M2-04 | Ubah jasa saat dikerjakan: jasa baru harga master, jasa lama tetap harga snapshot (K4) | PASS |
| M2-05 | Selesai & bayar: backflush 0,2 shampo + 0,15 masker, komisi snapshot, kembalian, status LUNAS/SELESAI | PASS |
| M2-06 | Kurang bayar / diskon > subtotal ditolak tanpa efek; QRIS dicatat sebesar total | PASS |
| M2-07 | Backflush tidak menggagalkan treatment saat stok kurang: stok minus + peringatan (A4) | PASS |
| M2-08 | Batal treatment LUNAS: butuh hak akses; stok & pembayaran dikembalikan, komisi 0, nomor tidak dipakai ulang | PASS |
| M2-09 | Batal treatment yang masih DIKERJAKAN: tanpa efek stok | PASS |
| M2-10 | Rollback atomik: kegagalan saat tulis pembayaran membatalkan seluruh posting treatment | PASS |

## Integrasi — Inventori (M3)

| ID | Skenario | Hasil |
|---|---|---|
| M3-01 | Penjualan: total & diskon baris, stok keluar, pembayaran, header = jumlah detail | PASS |
| M3-02 | Penjualan ditolak: stok kurang (akumulasi baris), produk tidak dijual, kurang bayar; nomor tidak terpakai | PASS |
| M3-03 | Batal penjualan: stok kembali, pembayaran dihapus, status BATAL; batal dua kali ditolak | PASS |
| M3-04 | Rollback atomik penjualan: gagal di tengah → tidak ada header/baris/stok, nomor tidak terpakai | PASS |
| M3-05 | Pembelian: stok masuk, harga pokok master tidak berubah (A8); batal ditolak bila barang sudah terpakai | PASS |
| M3-06 | Stock adjustment: qty +/−, nilai harga pokok, stok minus ditolak, batal | PASS |
| M3-07 | Kartu stok: saldo awal, mutasi berjalan, saldo akhir = stok produk = ledger | PASS |

## ViewModel transaksi (tanpa WPF)

| ID | Skenario | Hasil |
|---|---|---|
| VM-08 | Pembayaran: kembalian/kurang dihitung langsung; non-tunai dicatat sebesar total | PASS |
| VM-09 | Layar registrasi → 'Proses sekarang' → layar treatment: mulai, tambah jasa, diskon, bayar, cetak nota | PASS |
| VM-10 | Layar treatment: kurang bayar menampilkan error dan input kasir tidak hilang | PASS |
| VM-11 | Editor penjualan: produk digabung, total & kembalian, posting, tanya cetak | PASS |
| VM-12 | Hak akses: KASIR tidak bisa membatalkan penjualan yang sudah diposting | PASS |
| VM-13 | Kartu stok: pilih produk & periode, saldo awal/akhir tampil | PASS |

## Dashboard (M3.1)

| ID | Skenario | Hasil |
|---|---|---|
| DASH-01 | Dashboard harian: pendapatan jasa/produk, client selesai, antrian, rekap metode, capster; dokumen batal tidak dihitung | PASS |
| DASH-02 | Dashboard VM: teks KPI, tanggal Indonesia, tautan cepat membuka layar; toggle mode gelap | PASS |

## Nota (M3.2)

| ID | Skenario | Hasil |
|---|---|---|
| UNIT-16 | Nota: baris 'Jasa x qty  harga', tanpa nama capster; jasa sama digabung; header nama → alamat → telp | PASS |
| INT-LOGO-01 | Logo nota: migrasi 002, simpan/hapus; format & ukuran divalidasi; data salon lain tidak berubah | PASS |
| VM-14 | Pengaturan Salon: pilih logo → tersimpan & ikut dikirim ke printer bersama struk | PASS |

## Laporan (M4a–M4c)

| ID | Skenario | Hasil |
|---|---|---|
| M4-01 | Migrasi 003: snapshot biaya bahan per baris jasa saat LUNAS; batal mencatat cancelled_by/at + audit_log | PASS |
| M4-02 | Izin laporan: ADMIN semua izin baru; KASIR baru = view+customer; upgrade KASIR lama hanya dapat izin baru | PASS |
| M4-03 | Rekap Kas Harian: per metode (tagihan, bukan uang diterima), tunai di laci, daftar nota + referensi, batal tidak dihitung | PASS |
| M4-04 | Laporan Pendapatan: semua hari dalam periode (termasuk 0), jasa bruto/diskon/neto, produk, total = rekap metode = dashboard | PASS |
| M4-05 | Laporan Komisi: ringkasan & rincian per capster, filter capster = slip bertanda tangan, diskon tidak memotong komisi, batal = 0 | PASS |
| UNIT-17 | Periode laporan: pilihan cepat (minggu mulai Senin, 1–15 / 16–akhir, bulan lalu), teks periode, validasi | PASS |
| UNIT-18 | Export Excel: .xlsx valid (zip + XML), sheet per bagian, angka & tanggal bertipe, karakter khusus aman | PASS |
| UNIT-19 | Struk rekap kas 58 mm: per metode, total, tunai di laci, kolom paraf; semua baris ≤ 32 karakter | PASS |
| VM-15 | Halaman laporan: hak akses menu, periode cepat/manual, validasi tanggal, export Excel, PDF, struk rekap 58 mm | PASS |
| DASH-03 | Dashboard: komisi capster hanya tampil bagi user dengan izin laporan komisi | PASS |

| M4-06 | Jasa & Margin: per jasa bruto − komisi − bahan (snapshot); baris lama tanpa snapshot diestimasi & ditandai; diskon hanya di total | PASS |
| M4-07 | Produk & Laba Kotor: neto = bruto − diskon, HPP dari snapshot harga pokok (tidak berubah bila master diubah) | PASS |
| M4-08 | Laporan Stok: saldo awal/mutasi/akhir per produk = ledger, nilai stok, pemakaian bahan treatment, filter di bawah minimum | PASS |
| M4-09 | Laporan Pembatalan: semua jenis dokumen, waktu & user pembatal, alasan; registrasi dari treatment batal tidak dobel | PASS |
| M4-10 | Kunjungan Customer: status 30/60 hari, rata-rata jarak, total belanja (jasa+produk), jasa & capster terakhir, walk-in tidak ikut | PASS |
| VM-16 | M4b/M4c di aplikasi: menu per izin, pengaturan batas hari (validasi), penanda di Registrasi, riwayat di Master Customer | PASS |

INT-01 diperbarui: kini membuat database v1 sungguhan lalu meng-upgrade ke v3.

## Reset & Restore (M4d)

| ID | Skenario | Hasil |
|---|---|---|
| M4-11 | Reset (stok nol): semua transaksi & ledger terhapus, master/customer/user/pengaturan/logo/audit tetap, nomor nota kembali 0001, backup berisi data lama | PASS |
| M4-12 | Reset (pertahankan stok): stok setiap produk menjadi saldo awal OPENING di ledger; kartu stok mulai dari saldo awal; stok minus diberi peringatan | PASS |
| M4-13 | Pengaman reset: password/konfirmasi salah, role bukan ADMIN, backup gagal → ditolak tanpa perubahan; data > 14 hari diberi peringatan | PASS |
| M4-14 | Restore: validasi file (bukan DB / bukan aplikasi ini / versi lebih baru), backup otomatis DB aktif, data kembali, backup versi lama dimigrasikan, audit | PASS |
| VM-17 | Halaman Reset & Restore: tombol aktif hanya dengan password + 'RESET', konfirmasi akhir, hasil tampil; restore memicu login ulang | PASS |

## Verifikasi tambahan

| Item | Hasil |
|---|---|
| Import CLI terhadap **salinan `salon.sqlite` asli** | 10 produk (saldo awal total 566), 6 jasa, 3 karyawan, 1 customer (+ walk-in lama dilebur), user kasir; hash file lama sebelum = sesudah |
| Verifikasi statis XAML (XML valid, semua `StaticResource` terdefinisi, semua binding ke properti yang ada) | Lolos |
| File .xlsx laporan dibuka dengan pembaca independen (openpyxl) | Lolos: 4 sheet, tanggal bertipe tanggal, angka bertipe angka (`#,##0`), total tebal, header beku |
| Build & tampilan proyek WPF | **Belum diverifikasi**: harus di Windows (lihat `UAT_RESPONSIVE_CHECKLIST.md`) |
