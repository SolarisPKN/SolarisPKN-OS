# ITP draft — DO NOT SEND before maintainer contact and license approval

To: submit@bugs.debian.org
Subject: ITP: solarispkn-paste -- verified Linux file copying utility

Package: wnpp
Severity: wishlist
Owner: SolarisPKN <REPLACE_WITH_MONITORED_PUBLIC_EMAIL>

* Package name    : solarispkn-paste
  Version         : 0.3.0~alpha1
  Upstream Contact: SolarisPKN <REPLACE_WITH_MONITORED_PUBLIC_EMAIL>
* URL             : https://github.com/SolarisPKN/SolarisPKN-OS/tree/main/solarispkn-paste
* License         : GPL-3.0-only
  Programming Lang: Python (Linux CLI); C# (Windows GUI, not shipped in Debian)
  Description     : integrity-verified Linux command-line file copying utility

SolarisPKN-Paste stages file writes and verifies content digests while
using conservative storage-aware transfer scheduling. Its native Linux CLI
requires b3sum for BLAKE3; it does not install the Windows Explorer UI.
The current release is experimental and not suitable as the sole backup
of irreplaceable data.

ITP MUST be filed by the actual maintainer after legal ownership and
public email confirmation. Do not auto-send, and do not claim a bug ID.
