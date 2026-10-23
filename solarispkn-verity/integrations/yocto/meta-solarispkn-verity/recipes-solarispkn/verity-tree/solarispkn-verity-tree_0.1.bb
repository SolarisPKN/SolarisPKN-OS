# SPDX-License-Identifier: GPL-2.0-only
SUMMARY = "SolarisPKN-Verity dm-verity format-1 Merkle tree builder (AF_ALG)"
DESCRIPTION = "Userspace BLAKE3/SHA256 Merkle tree builder using kernel AF_ALG; not an alternative dm-verity target."
HOMEPAGE = "https://github.com/SolarisPKN/SolarisPKN-OS"
LICENSE = "GPL-2.0-only"
LIC_FILES_CHKSUM = "file://verity-tree.c;beginline=1;endline=1;md5=fcab174c20ea2e2bc0be64b493708266"

# The layer is distributed alongside the solarispkn-verity tools directory
# in the SolarisPKN-OS monorepo. The source is not copied from EXO labs.
FILESEXTRAPATHS:prepend := "${THISDIR}/../../../../../tools/verity-tree:"
SRC_URI = "file://verity-tree.c"

S = "${WORKDIR}"

do_compile() {
    ${CC} ${CPPFLAGS} ${CFLAGS} "${S}/verity-tree.c" \
        -o solarispkn-verity-tree ${LDFLAGS}
}

do_install() {
    install -d "${D}${sbindir}"
    install -m 0755 solarispkn-verity-tree "${D}${sbindir}/solarispkn-verity-tree"
}

# Kernel crypto API algorithm provider is an integration-time dependency.
# Do not hard-code a private lab's module name as a package dependency.
