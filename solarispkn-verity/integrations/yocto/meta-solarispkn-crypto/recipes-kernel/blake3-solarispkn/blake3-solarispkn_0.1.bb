SUMMARY = "Experimental SolarisPKN BLAKE3 Crypto API SHASH with guarded SIMD"
DESCRIPTION = "External Linux kernel Crypto API module; upstream CC0 BLAKE3 C and x86 SIMD (SSE2/SSE4.1/AVX2, AVX512 opt-in), portable fallback"
LICENSE = "GPL-2.0-only & CC0-1.0"
LIC_FILES_CHKSUM = "file://LICENSE_CC0;md5=65d3616852dbf7b1a6d4b53b00626032 file://spkn_blake3_shash.c;beginline=1;endline=1;md5=9fda599b1b05ee90019d59b522147ec7"
inherit module
SRC_URI = "file://Makefile file://LICENSE_CC0 file://spkn_kernel_compat.h file://spkn_blake3_shash.c file://blake3_dispatch_kernel.c file://blake3.h file://blake3_impl.h file://blake3.c file://blake3_portable.c file://blake3_sse2_x86-64_unix.S file://blake3_sse41_x86-64_unix.S file://blake3_avx2_x86-64_unix.S file://blake3_avx512_x86-64_unix.S"
S = "${UNPACKDIR}"
# Portable C on non-x86; tests on ARM64/RISC-V pending.
