# Request for Sponsorship — preparation only, NOT submitted

Package sources: solarispkn-paste 0.3.0~alpha1-1 and
solarispkn-verity 0.6.0~rc1-1 (independent Debian source packages).

Source VCS: https://github.com/SolarisPKN/SolarisPKN-OS

Before filing a separate RFS for **each** package:
- Replace the GitHub noreply build-only identity with monitored
  Debian Maintainer and upstream contact addresses.
- File WNPP ITPs and include their assigned bug IDs in changelogs.
- Publish upstream release tags and tarball source; test debian/watch.
- Audit lintian findings, architecture and license correctness, and run
  clean Debian sid builds.
- Produce and sign .dsc/.changes with the **maintainer's own** GPG key.
- Upload complete source to mentors.debian.net or an approved source
  hosting destination, and link it in sponsorship-requests.

Never use the private SolarisPKN-Paste APT-repository signing key to
impersonate a Debian maintainer identity. That key signs a separate
third-party APT repository and is NOT a substitute for personal Debian
package sponsorship.

References: https://mentors.debian.net/sponsors/
