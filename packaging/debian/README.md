# Debian upstream packaging candidates (SolarisPKN-OS)

The upstream monorepo hosts independent projects. **Neither is currently
accepted into the official Debian archive.** The signed, third-party APT
repository distributed by SolarisPKN is a different distribution channel.

## Source packages

| Source | Debian binaries | Status |
| --- | --- | --- |
| solarispkn-paste | solarispkn-paste (Python Linux CLI) | Debian 3.0 (quilt) packaging; not the Windows UI |
| solarispkn-verity | solarispkn-verity (Python CLI) and solarispkn-verity-tree (AF_ALG C) | Userspace experimental only; kernel module not in Debian package |

Each module has **its own** license / copyright. Paste is GPL-3.0-only;
Verity's Linux integration/C tool is GPL-2.0-only, Python is MIT and
bundled upstream BLAKE3 C/assembly is CC0-1.0.

Run builds from a Debian sid environment with required build dependencies:

~~~sh
sudo apt install debhelper dh-sequence-python3 python3-all python3-setuptools \
  dpkg-dev devscripts lintian build-essential xz-utils b3sum openssl
bash packaging/debian/build-source.sh solarispkn-paste
bash packaging/debian/build-source.sh solarispkn-verity
~~~

The build script generates .orig.tar.xz, .debian.tar.xz, .dsc, .changes,
.buildinfo and .deb files under packaging/debian/dist/<module>/.
It builds in an isolated temporary directory, never modifying the
installed OS or publishing a kernel module. GitHub Actions also runs
Debian sid packaging, lintian, apt-install and CLI smoke tests.

## Submitting to official Debian requires additional steps

1. Confirm **copyright holder(s), full source provenance**, GPL-3.0-only
   for Paste and pre-existing GPL-2.0/MIT/CC0 portions of Verity.
2. Provide a real, **monitored public email** for Debian Maintainer and ITP
   Owner; the GitHub no-reply address currently used in the build metadata
   is NOT sufficient for long-term package maintenance.
3. Tag module-specific upstream releases and publish **standalone upstream
   source tarballs**, update debian/watch, and verify reproducibility.
4. Run clean sid build / lintian / tests and review warnings with a
   sponsor. Test on multiple architectures if including C binaries.
5. File an **ITP (Intent To Package)** bug against Debian WNPP.
6. Sign the source upload with the maintainer's own OpenPGP identity;
   register at mentors.debian.net; upload source and find a Debian
   Developer sponsor. A sponsor reviews/approves before NEW queue review.

No WNPP ITP or mentors upload has been submitted. Template drafts are under
ITP-PASTE.md, ITP-VERITY.md, and RFS-DRAFT.md. Never invent Debian bug
numbers, sponsor approval, or official status.

Official process: https://mentors.debian.net/intro-maintainers/
Debian Policy: https://www.debian.org/doc/debian-policy/
