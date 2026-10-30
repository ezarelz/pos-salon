# UAT — Responsivitas UI & Fungsional (M1–M3)

Jalankan di Windows. Ubah skala di **Settings → System → Display → Scale**, resolusi di **Display resolution**.
Hasil: ✔ / ✘ + catatan singkat.

## Matriks layar

| # | Resolusi | Skala | Area efektif (DIP) | Hasil |
|---|---|---|---|---|
| R1 | 1920×1080 | 100% | 1920×1040 | |
| R2 | 1920×1080 | 150% | 1280×693 | |
| R3 | 1366×768 | 100% | 1366×728 | |
| R4 | 1366×768 | 125% | 1093×574 | |
| R5 | 1280×720 | 100% | 1280×680 | |
| R6 | 1280×720 | 125% | 1024×536 | |
| R7 | 1280×720 | 150% | 853×440 | |

## Skenario (ulangi untuk setiap baris matriks)

| ID | Test Step | Expected Test Result |
|---|---|---|
| UI-01 | Buka aplikasi → layar login | Login tampil utuh, tombol Masuk terlihat |
| UI-02 | Login → main window | Jendela muat di layar; maximize otomatis di R4–R7 |
| UI-03 | Resize main window ke ukuran minimum | Menu menjadi ikon saja; tidak ada tombol yang hilang |
| UI-04 | Lebarkan kembali ke > 1000 DIP | Menu kembali lengkap |
| UI-05 | Master Produk → Tambah | Editor muat di layar; tombol Simpan/Batal terlihat |
| UI-06 | Editor Produk: perkecil jendela | Form menjadi 1 kolom; konten bisa di-scroll; footer tetap terlihat |
| UI-07 | Editor Jasa: tambah 6 bahan resep | Grid resep bisa di-scroll; margin & tombol Simpan terlihat |
| UI-08 | Simpan dengan input salah (harga "abc") | Pesan error tampil di footer, di samping tombol |
| UI-09 | Daftar master di jendela sempit | Toolbar turun ke baris kedua; grid tetap terbaca |
| UI-10 | Navigasi keyboard: Tab di editor, Enter, Esc | Fokus berurutan; Enter = Simpan; Esc = Batal |
| UI-11 | Daftar: Ctrl+N, Enter, Delete, F5 | Tambah / Ubah / Hapus / Muat ulang |
| UI-12 | Pindah jendela antar monitor beda skala (bila ada) | Teks tetap tajam (Per-Monitor DPI) |

## Fungsional singkat M1

| ID | Test Step | Expected Test Result |
|---|---|---|
| F-01 | Login admin/admin | Diminta ganti password; setelah ganti masuk dashboard |
| F-02 | Pengaturan → Import Data Lama → pilih salon.sqlite lama | Ringkasan import tampil; file lama tidak berubah |
| F-03 | Master Produk → tandai Shampo "Bahan habis pakai" | Tersimpan |
| F-04 | Master Jasa → Cuci Rambut → resep Shampo 0,1 | Tampil "≈ 20 ml" (bila isi 200 ml); margin terhitung |
| F-05 | Dashboard | Jasa tanpa resep & stok di bawah minimum tampil |
| F-06 | Role & Hak Akses → tambah role baru | Role tersimpan; user dengan role itu hanya melihat menu sesuai hak |
| F-07 | Login sebagai kasir | Menu Master Produk/User tidak tampil |
| F-08 | Backup Database → pilih folder | File `mspasalon_yyyyMMdd_HHmmss.db` terbentuk |

## Fungsional M2/M3 (uji di Windows)

| ID | Test Step | Expected Test Result | Hasil |
|---|---|---|---|
| F-10 | Registrasi Customer → Tambah: customer, Cuci Rambut + capster → Simpan | Nomor `YYMM/RG/0001`, status MENUNGGU; ditawarkan "Proses treatment sekarang?" | |
| F-11 | Jawab Ya | Tab Proses Treatment terbuka, registrasi terpilih di antrian, pratinjau bahan (Shampo −0,1) tampil | |
| F-12 | Mulai Treatment → + Tambah Jasa (Creambath, capster lain) → Simpan Perubahan Jasa | Subtotal naik; Shampo menjadi −0,2; Masker muncul | |
| F-13 | Diskon 5.000; Tunai; uang diterima kurang dari total → Selesai & Bayar | Pesan "Uang diterima…"; diskon & uang diterima tidak hilang | |
| F-14 | Tombol "Uang pas"/isi cukup → Selesai & Bayar → Cetak nota | Status LUNAS; antrian kosong; nota 58 mm tercetak penuh selebar kertas (32 karakter), tidak terpotong | |
| F-15 | Metode QRIS + referensi | Tidak ada kembalian; referensi tercetak di nota | |
| F-16 | Stok bahan dibuat kurang (adjustment) lalu selesaikan treatment | Pratinjau merah + peringatan; treatment tetap LUNAS; stok minus | |
| F-17 | Riwayat Treatment → pilih LUNAS → Batalkan (admin, isi alasan) | Status BATAL; stok bahan kembali; login kasir: tombol batal nonaktif | |
| F-18 | Penjualan Produk → Tambah: produk sama 2× → Bayar & Simpan | Baris digabung; kembalian tampil; stok berkurang; tawaran cetak | |
| F-19 | Penjualan dengan qty melebihi stok | Ditolak; nomor FJ tidak terpakai | |
| F-20 | Pembelian → supplier, produk, qty, harga → Simpan | Stok bertambah; harga pokok master tidak berubah | |
| F-21 | Stock Adjustment −qty melebihi stok | Ditolak; adjustment wajar tersimpan dengan alasan | |
| F-22 | Kartu Stok → pilih Shampo, periode bulan ini | Saldo awal, mutasi (pembelian/treatment/adjustment), saldo akhir = stok di master | |
| F-23 | Ulangi F-11..F-14 pada R7 (853×440 DIP) | Antrian tetap terlihat; isi detail bisa di-scroll; tombol Selesai & Bayar selalu terlihat | |

## Dashboard & mode gelap (M3.1)

| ID | Test Step | Expected Test Result | Hasil |
|---|---|---|---|
| F-24 | Selesaikan 1 treatment + 1 penjualan → buka Dashboard | Pendapatan, client selesai, transaksi terakhir, rekap metode terisi | |
| F-25 | Batalkan penjualan tadi → Dashboard (muat ulang) | Pendapatan turun; "1 batal" tampil | |
| F-26 | Klik tombol cepat "Proses Treatment" | Tab Proses Treatment terbuka | |
| F-27 | Menu bawah → Mode Gelap; buka Produk, editor Jasa, Proses Treatment, Kartu Stok | Semua teks terbaca; ComboBox, tabel, tab, scrollbar ikut gelap | |
| F-28 | Tutup aplikasi → buka lagi | Login & main window langsung mode gelap; Mode Terang mengembalikan | |

## Nota (M3.2)

| ID | Test Step | Expected Test Result | Hasil |
|---|---|---|---|
| F-29 | Pengaturan Salon → Pilih Logo (PNG) | Pratinjau tampil; status "Logo nota tersimpan" | |
| F-30 | Treatment Barber + Cuci Rambut ×2 → bayar → cetak 58 mm | Logo di tengah; nama, alamat, telp; `Barber x 1`, `Cuci Rambut x 2`; tanpa nama capster | |
| F-31 | Hapus Logo → cetak ulang dari Riwayat | Nota tanpa logo, header teks tetap | |
