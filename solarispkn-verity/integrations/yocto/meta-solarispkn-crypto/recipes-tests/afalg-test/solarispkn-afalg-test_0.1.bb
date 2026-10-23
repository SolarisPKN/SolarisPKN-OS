SUMMARY = "SolarisPKN BLAKE3 kernel AF_ALG tests"
DESCRIPTION = "Six independent known-answer vectors through Linux AF_ALG Crypto API"
LICENSE = "MIT"
LIC_FILES_CHKSUM = "file://spkn-afalg-test.c;beginline=1;endline=1;md5=234d7d4edd08962c0144e4604050e0b6"
SRC_URI = "file://spkn-afalg-test.c"
S = "${UNPACKDIR}"
B = "${WORKDIR}/build"
do_compile() {
    install -d "${B}"
    ${CC} ${CFLAGS} "${S}/spkn-afalg-test.c" ${LDFLAGS} -o "${B}/spkn-afalg-test"
}
do_install() {
    install -d "${D}${bindir}"
    install -m 0755 "${B}/spkn-afalg-test" "${D}${bindir}/spkn-afalg-test"
}
