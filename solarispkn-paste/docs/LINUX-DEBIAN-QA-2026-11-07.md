# 🐧 SolarisPKN-Paste · Debian package and native Linux QA

**November 7, 2026 · Experimental Linux CLI v0.3.0-alpha.1**

## Test environment

- OS: **Debian GNU/Linux 13 Trixie** (13.7) under **WSL2**; kernel 6.6.87.2-microsoft-standard-WSL2, Python 3.13.5.
- Source: solarispkn-paste/linux/solarispkn_paste/cli.py. Independent from Windows WinForms; no Windows UI on Linux.
- Package: dist/solarispkn-paste_0.3.0~alpha1-1_all.deb, architecture all; Depends: python3 >= 3.10 and b3sum.
- Packaging performed unprivileged with dpkg-deb. **No apt/dpkg installation into the Debian system was performed:** the WSL user does not have passwordless sudo. The .deb was extracted to a temporary directory and the packaged Python module was imported and executed.
- BLAKE3 integrity was genuinely tested: downloaded the Debian 13 **official** b3sum 1.8.1-2 package to a throwaway directory with apt-get download, extracted it with dpkg-deb and ran the binary on source and destination. The dependency was not permanently installed.
- The user-facing installer is a regular .deb: sudo apt install ./dist/solarispkn-paste_0.3.0~alpha1-1_all.deb, which should resolve b3sum from enabled OS repositories. This complete privileged installation flow remains **untested**.

## Functional QA

| Suite | Result | Important limitation |
|---|---|---|
| Python unittest: safety, overwrite, SHA-256, dry-run, symlinks, recovery backup, profile, fallback, benchmark cleanup | **15/15 PASS** | Synthetic tests, no production datasets |
| Debian package build and dpkg-deb info/contents | **PASS** | Does not equal successful apt installation |
| Extracted .deb module import / hardware profile | **PASS** | No system-wide install |
| BLAKE3 via official Debian b3sum, real copied data | **PASS** | Tiny synthetic file, not endurance |
| SHA-256 verified, 2 MiB benchmark after package extraction | **PASS** | Short cache-sensitive workload |
| Original SVG and 12-frame GIF | **PASS** | Format inspection, not a human visual audit |
| Ubuntu 24.04 / 26.04 actual apt installation | **NOT RUN** | Package dependencies exist, but no Ubuntu distro used here |
| Linux file ACL, xattr, hardlink, sparse layout, power-cut resilience | **NOT RUN** | Experimental implementation intentionally documents gaps |

The suite ended with **LINUX_RELEASE_QA_PASS**, exit 0. Full local evidence is retained outside the public source tree in the PC's SolarisPKN-Paste research reports folder. Do not copy host-specific private logs to GitHub.

## Newly measured native Linux CLI microbenchmark

Fixture: 16 MiB regular synthetic file on WSL2 Debian ext4 VHDX, destination on same filesystem; **three passes per mode**, SHA-256 from source and destination plus file fsync included in wall time. Media classification was conservative/unknown because WSL2 does not expose the actual host HDD/SSD behind the VHDX.

| Engine | Round 1 | Round 2 | Round 3 | Median |
|---|---:|---:|---:|---:|
| Python buffered 1 MiB | 40.87 MiB/s | 43.84 MiB/s | 42.92 MiB/s | **42.92 MiB/s** |
| Linux copy_file_range | 36.23 MiB/s | 60.46 MiB/s | 47.08 MiB/s | **47.08 MiB/s** |

**Caution:** source and target are small and frequently cached; this is a **copy-plus-SHA256 end-to-end application microbenchmark**, not raw hardware disk bandwidth or sustained performance. Three passes with visible variance do not show a conclusive winner. They are separate from the Windows NVMe/SATA/HDD benchmarks and must not be compared as like-for-like results.

Run it locally:

~~~bash
cd solarispkn-paste/linux
python3 -m unittest discover -s tests -p 'test_*.py' -v
cd ..
bash packaging/build-deb.sh
bash tests/package-smoke.sh
python3 solarispkn_paste/cli.py benchmark --root "$HOME" --size-mib 16 --rounds 3 --hash sha256 --engine buffered
python3 solarispkn_paste/cli.py benchmark --root "$HOME" --size-mib 16 --rounds 3 --hash sha256 --engine copyfile-range
~~~

## 🛡️ Security boundary

- Exclusive temporary staging, integrity check before publication, no overwrite without explicit --overwrite.
- For overwrites, an older version remains under .solarispkn-recovery, not silent destruction.
- Symbolic links in sources and destinations are rejected.
- Benchmark uses an owned temporary directory inside the supplied root with cleanup, file sizes 1–256 MiB and 1–5 rounds; requires a free-space reserve.
- No root services, shell extensions, direct raw-disk writes or automatic USB benchmarks.
- Uncertain WSL2/RAID media is processed conservatively instead of blindly assuming SSD.
- **Limitations:** Linux CLI presently lacks comprehensive ACL/xattr/SELinux preservation and full transactional rollback. Power loss or process termination while publishing directories is not covered.
