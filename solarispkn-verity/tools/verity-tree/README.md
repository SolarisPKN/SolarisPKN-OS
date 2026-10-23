# SolarisPKN-Verity tree generator (experimental)

**Goal:** generate Linux dm-verity format 1 Merkle trees using BLAKE3 or SHA-256, without requiring either algorithm in OpenSSL or in veritysetup's userspace backend.

This tool does not replace or patch the upstream kernel dm-verity target. It uses the Linux AF_ALG interface to a hash driver registered in the Kernel Crypto API.

## Build requirements (Linux)
- C compiler and Linux UAPI headers.
- Kernel AF_ALG hashing enabled: CONFIG_CRYPTO_USER_API and CONFIG_CRYPTO_USER_API_HASH.
- Algorithm available in /proc/crypto (our experimental module loads with "modprobe blake3_crypto_lab").
- **Disposable guest for first tests.** The experimental kernel module is not audited.

Commands:

    make -C tools/verity-tree
    tools/verity-tree/solarispkn-verity-tree selftest blake3

The BLAKE3 selftest checks 11 official BLAKE3 test-vector digests through AF_ALG, from https://github.com/BLAKE3-team/BLAKE3/tree/master/test_vectors

## Operations

    TREE=tools/verity-tree/solarispkn-verity-tree
    $TREE format blake3 /tmp/data.img /tmp/NEW-hash.img 0011223344556677
    $TREE verify blake3 /tmp/data.img /tmp/NEW-hash.img TRUSTED_ROOT_HEX 0011223344556677
    $TREE table blake3 /dev/loop0 /dev/loop1 DATA_BLOCKS TRUSTED_ROOT_HEX 0011223344556677

Hash path must NOT exist when formatting. This is an intentional protection against overwriting system block devices.

Use "-" for an empty salt or omit salt. Nonempty salt is an even-length hexadecimal string. dm-verity format 1 hashes each block as HASH(salt || whole 4096-byte block). This is independent of BLAKE3's own internal chunk tree.

The "table" operation writes one line for dmsetup. It does not create a mapper, change any disk or authenticate the root digest.

## Output layout
- Version 1; SHA-256 or BLAKE3, digest size 32.
- 4096-byte data blocks and 4096-byte hash blocks.
- Raw Merkle tree with NO verity superblock.
- Hash start block 0; levels are stored top to bottom, zero-padded blocks.
- Blockwise streaming I/O, not the entire tree held in memory.
- One-data-block special case: no Merkle levels; root = HASH(salt || data). A blank backing block is created solely so the hash file can be used as a loop device.

## Security and limitations
The trusted root hash must be authenticated out-of-band; this tool does not create Secure Boot signatures, protect root hashes or validate the entire boot chain.

Linux AF_ALG is a user-facing interface, but the kernel hash driver must still be present. If you see "AF_ALG unavailable", check CONFIG_CRYPTO_USER_API_HASH. BLAKE3 showing in /proc/crypto does not automatically mean the AF_ALG interface is enabled.

Current source is **experimental, not compiled or executed on the user's VM yet**. The lab's external BLAKE3 kernel adapter has unresolved shash state lifecycle/export concerns that must be fixed before production use.
