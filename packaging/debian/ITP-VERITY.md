# ITP draft — DO NOT SEND before maintainer contact and copyright review

To: submit@bugs.debian.org
Subject: ITP: solarispkn-verity -- experimental userspace dm-verity Merkle tools

Package: wnpp
Severity: wishlist
Owner: SolarisPKN <REPLACE_WITH_MONITORED_PUBLIC_EMAIL>

* Package name    : solarispkn-verity
  Version         : 0.6.0~rc1
  Upstream Contact: SolarisPKN <REPLACE_WITH_MONITORED_PUBLIC_EMAIL>
* URL             : https://github.com/SolarisPKN/SolarisPKN-OS/tree/main/solarispkn-verity
* License         : GPL-2.0-only (C tool and kernel glue), MIT (Python), CC0-1.0 (bundled BLAKE3)
  Programming Lang: Python, C
  Description     : userspace dm-verity Merkle and signature verification tools

The source builds the solarispkn-verity userspace CLI and the separate
solarispkn-verity-tree binary, which uses Linux AF_ALG. This proposal does
NOT install the experimental BLAKE3 kernel Crypto API provider, does not
enable a verified boot chain, and is NOT a production Secure Boot release.

Review the mixed-license copyright inventory and file-by-file third-party
provenance before sending the ITP. No bug ID has been assigned yet.
