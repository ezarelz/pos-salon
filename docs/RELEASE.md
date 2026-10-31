# POS Salon — Rilis & Instalasi

## A. Untuk pemilik / kasir (memasang aplikasi)

### 1. Prasyarat PC kasir
| Kebutuhan | Keterangan |
|---|---|
| Windows 10/11 64-bit | |
| **.NET 10 Desktop Runtime (x64)** | Unduh di https://dotnet.microsoft.com/download/dotnet/10.0 → bagian *.NET Desktop Runtime 10* → **Windows x64**. Installer POS Salon memeriksanya dan menawarkan membuka halaman ini bila belum ada. |
| Hak admin | Hanya saat memasang (aplikasi dipasang di `C:\Program Files\POS Salon`). Memakai aplikasi tidak perlu admin. |

### 2. Pasang
1. Jalankan `POSSalon-Setup-<versi>.exe`.
2. Bila muncul **"Windows protected your PC"** (SmartScreen, karena installer tidak bertanda tangan digital): klik **More info → Run anyway**.
3. Ikuti wizard; centang *Buat shortcut di Desktop* bila perlu.
4. Login pertama: **admin / admin** → wajib ganti password.

### 3. Data
| Item | Lokasi |
|---|---|
| Database | `%LOCALAPPDATA%\MSPASalon\mspasalon.db` (per user Windows) |
| Backup otomatis (sebelum upgrade, reset, restore) | `%LOCALAPPDATA%\MSPASalon\backup\` |
| Log error | `%LOCALAPPDATA%\MSPASalon\logs\` |

Installer, upgrade, dan uninstall **tidak pernah** menyentuh folder data.

### 4. Upgrade ke versi baru
Jalankan installer versi baru di atas versi lama (aplikasi yang sedang terbuka akan ditutup). Saat pertama kali dibuka,
bila struktur database berubah, aplikasi **otomatis membackup database lama** (`…_sebelum-upgrade-vN.db`) sebelum memperbaruinya.
Bila perlu kembali: **Pengaturan → Restore Backup** dan pilih file tersebut.

### 5. Pindah ke PC lain
1. PC lama: **Backup Database** (menu kiri bawah) → salin file `.db` ke flashdisk.
2. PC baru: pasang installer → login → **Pengaturan → Restore Backup** → pilih file tadi.

### 6. Uninstall
Windows Settings → Apps → POS Salon → Uninstall. Data tetap ada di `%LOCALAPPDATA%\MSPASalon`; memasang ulang akan langsung membacanya.

### 7. Go-live (setelah uji coba)
Login ADMIN → **Pengaturan → Reset Data Transaksi** untuk menghapus transaksi uji coba (BUSINESS_RULES §16).

## B. Untuk developer (membuat rilis)

### 1. Prasyarat
.NET 10 SDK (lihat `global.json`), Inno Setup 6 (`winget install JRSoftware.InnoSetup`).

### 2. Membuat installer di laptop
```powershell
.\build-installer.ps1             # test → dotnet publish → ISCC
.\build-installer.ps1 -SkipTests
```
Hasil: `installer\Output\POSSalon-Setup-<versi>.exe`. Versi diambil dari `<Version>` di `Directory.Build.props`.

| Langkah | Detail |
|---|---|
| Publish | `dotnet publish src/MSPASalon.Desktop -p:PublishProfile=win-x64` → `publish\win-x64\POSSalon.exe` (framework-dependent, single file, ReadyToRun; **tanpa** trimming karena WPF tidak mendukung) |
| Installer | `installer\POSSalon.iss` — per-machine, `AppId` tetap (**jangan diubah**: itu yang membuat versi baru meng-upgrade, bukan terpasang dobel) |

### 3. Rilis lewat GitHub
1. Naikkan `<Version>` di `Directory.Build.props` (mis. `2.0.1`) + entri `CHANGELOG.md`, merge ke `main`.
2. Buat tag dengan versi yang sama lalu push:
   ```powershell
   git tag v2.0.1
   git push origin v2.0.1
   ```
3. Workflow **Build & Installer** (`.github/workflows/build.yml`) menjalankan test, build seluruh solution (termasuk WPF),
   membuat installer, memeriksa tag = versi, lalu membuat **GitHub Release** dengan `POSSalon-Setup-2.0.1.exe`.

Setiap push ke `main` dan setiap Pull Request juga menjalankan workflow ini dan menyimpan installer sebagai *artifact*
(tab **Actions** → run → *Artifacts*), sehingga build WPF selalu terverifikasi di Windows.
