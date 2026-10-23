// SPDX-License-Identifier: GPL-2.0-only
// SolarisPKN-Verity userspace dm-verity format-1 tree tool.
// Original integration: SolarisPKN contributors.
// No Linux kernel or upstream BLAKE3 source is embedded in this file.
//
// EXPERIMENTAL: requires Linux AF_ALG and a registered hash driver. This is
// intentionally separate from the dm-verity block-verification engine.
#define _GNU_SOURCE
#include <errno.h>
#include <fcntl.h>
#include <inttypes.h>
#include <limits.h>
#include <linux/fs.h>
#include <linux/if_alg.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <sys/types.h>
#include <unistd.h>

#define BLOCK_SIZE 4096U
#define DIGEST_SIZE 32U
#define HASHES_PER_BLOCK (BLOCK_SIZE / DIGEST_SIZE)
#define MAX_LEVELS 32U
#define MAX_SALT 256U

struct layout {
    uint64_t data_blocks;
    uint64_t levels;
    uint64_t count[MAX_LEVELS];  // leaf level is index 0
    uint64_t pos[MAX_LEVELS];    // on-disk order: top level first
    uint64_t hash_blocks;
};

struct hash_ctx {
    int tfm;
    const unsigned char *salt;
    size_t salt_len;
};

static void usage(const char *p)
{
    fprintf(stderr,
        "SolarisPKN-Verity (EXPERIMENTAL), dm-verity format 1 / 4096-byte blocks\n"
        "Usage:\n"
        "  %s selftest blake3\n"
        "  %s format <blake3|sha256> <data-file> <NEW-hash-file> [salt-hex|-]\n"
        "  %s verify <blake3|sha256> <data-file> <hash-file> <trusted-root-hex> [salt-hex|-]\n"
        "  %s table <blake3|sha256> <data-loop> <hash-loop> <data-blocks> <trusted-root-hex> [salt-hex|-]\n"
        "\n"
        "The hash file is a RAW dm-verity tree with NO superblock. The root hash\n"
        "must be authenticated separately; this utility DOES NOT implement Secure Boot.\n",
        p, p, p, p);
}

static bool supported(const char *alg)
{
    return strcmp(alg, "blake3") == 0 || strcmp(alg, "sha256") == 0;
}

static int hexval(int ch)
{
    if (ch >= '0' && ch <= '9') return ch - '0';
    if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
    if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
    return -1;
}

static int decode_hex(const char *s, unsigned char *out, size_t max,
                      size_t *outlen)
{
    size_t len;
    if (strcmp(s, "-") == 0) {
        *outlen = 0;
        return 0;
    }
    len = strlen(s);
    if ((len & 1U) || len / 2 > max) return -1;
    for (size_t i = 0; i < len / 2; i++) {
        int hi = hexval((unsigned char)s[i * 2]);
        int lo = hexval((unsigned char)s[i * 2 + 1]);
        if (hi < 0 || lo < 0) return -1;
        out[i] = (unsigned char)((hi << 4) | lo);
    }
    *outlen = len / 2;
    return 0;
}

static void print_hex(const unsigned char *data, size_t n)
{
    for (size_t i = 0; i < n; i++) printf("%02x", data[i]);
}

static uint64_t ceiling_div(uint64_t n, uint64_t divisor)
{
    return n / divisor + (n % divisor != 0);
}

static int layout_init(struct layout *l, uint64_t data_blocks)
{
    uint64_t n = data_blocks;
    memset(l, 0, sizeof(*l));
    if (!data_blocks || data_blocks > UINT64_MAX / (BLOCK_SIZE / 512U))
        return -1;
    l->data_blocks = data_blocks;

    // As in dm-verity/cryptsetup: a one-block image has no Merkle levels;
    // its root hash is the hash of that data block.
    while (n > 1) {
        if (l->levels >= MAX_LEVELS) return -1;
        n = ceiling_div(n, HASHES_PER_BLOCK);
        l->count[l->levels++] = n;
    }
    for (size_t level = (size_t)l->levels; level > 0; level--) {
        size_t i = level - 1;
        l->pos[i] = l->hash_blocks;
        if (l->count[i] > UINT64_MAX - l->hash_blocks) return -1;
        l->hash_blocks += l->count[i];
    }
    if (l->hash_blocks > INT64_MAX / BLOCK_SIZE) return -1;
    return 0;
}

static int file_size(int fd, uint64_t *bytes)
{
    struct stat st;
    if (fstat(fd, &st)) return -1;
    if (S_ISREG(st.st_mode)) {
        if (st.st_size < 0) return -1;
        *bytes = (uint64_t)st.st_size;
        return 0;
    }
    if (S_ISBLK(st.st_mode))
        return ioctl(fd, BLKGETSIZE64, bytes);
    errno = EINVAL;
    return -1;
}

static int io_full(int fd, void *buf, size_t len, uint64_t at, bool write_it)
{
    size_t done = 0;
    if (at > INT64_MAX || len > INT64_MAX - at) {
        errno = EOVERFLOW;
        return -1;
    }
    while (done < len) {
        ssize_t r = write_it
            ? pwrite(fd, (const unsigned char *)buf + done, len - done,
                     (off_t)(at + done))
            : pread(fd, (unsigned char *)buf + done, len - done,
                    (off_t)(at + done));
        if (r < 0 && errno == EINTR) continue;
        if (r <= 0) {
            if (r == 0) errno = EIO;
            return -1;
        }
        done += (size_t)r;
    }
    return 0;
}

static int open_hash(struct hash_ctx *ctx, const char *alg,
                     const unsigned char *salt, size_t salt_len)
{
    struct sockaddr_alg sa;
    ctx->tfm = -1;
    ctx->salt = salt;
    ctx->salt_len = salt_len;
    memset(&sa, 0, sizeof(sa));
    sa.salg_family = AF_ALG;
    memcpy(sa.salg_type, "hash", 5);
    if (strlen(alg) >= sizeof(sa.salg_name)) return -1;
    memcpy(sa.salg_name, alg, strlen(alg) + 1);

    ctx->tfm = socket(AF_ALG, SOCK_SEQPACKET | SOCK_CLOEXEC, 0);
    if (ctx->tfm < 0 || bind(ctx->tfm, (struct sockaddr *)&sa, sizeof(sa))) {
        perror("AF_ALG: Linux hash API unavailable or hash not registered");
        if (ctx->tfm >= 0) close(ctx->tfm);
        ctx->tfm = -1;
        return -1;
    }
    return 0;
}

static int hash_one(struct hash_ctx *ctx, const unsigned char *buf,
                    size_t bytes, unsigned char digest[DIGEST_SIZE])
{
    int op = accept4(ctx->tfm, NULL, NULL, SOCK_CLOEXEC);
    if (op < 0) return -1;
    if (ctx->salt_len) {
        ssize_t n = send(op, ctx->salt, ctx->salt_len, MSG_MORE);
        if (n != (ssize_t)ctx->salt_len) goto fail;
    }
    // Complete the AF_ALG message; the salt was sent with MSG_MORE.
    if (send(op, buf, bytes, 0) != (ssize_t)bytes) goto fail;
    if (read(op, digest, DIGEST_SIZE) != DIGEST_SIZE) goto fail;
    close(op);
    return 0;
fail:
    close(op);
    errno = EIO;
    return -1;
}

static int tree_process(struct hash_ctx *hc, int data_fd, int hash_fd,
                        const struct layout *l, bool create,
                        unsigned char root[DIGEST_SIZE])
{
    unsigned char src[BLOCK_SIZE], block[BLOCK_SIZE], stored[BLOCK_SIZE];
    unsigned char digest[DIGEST_SIZE];

    for (uint64_t level = 0; level < l->levels; level++) {
        uint64_t input_count = level ? l->count[level - 1] : l->data_blocks;
        uint64_t input_base = level ? l->pos[level - 1] : 0;
        int input_fd = level ? hash_fd : data_fd;

        for (uint64_t b = 0; b < l->count[level]; b++) {
            memset(block, 0, sizeof(block));
            for (uint64_t k = 0; k < HASHES_PER_BLOCK; k++) {
                uint64_t idx = b * HASHES_PER_BLOCK + k;
                if (idx >= input_count) break;
                uint64_t bytepos = (input_base + idx) * BLOCK_SIZE;
                if (io_full(input_fd, src, sizeof(src), bytepos, false))
                    return -1;
                if (hash_one(hc, src, sizeof(src), digest))
                    return -1;
                memcpy(block + k * DIGEST_SIZE, digest, DIGEST_SIZE);
            }
            uint64_t output_at = (l->pos[level] + b) * BLOCK_SIZE;
            if (create) {
                if (io_full(hash_fd, block, sizeof(block), output_at, true))
                    return -1;
            } else {
                if (io_full(hash_fd, stored, sizeof(stored), output_at, false))
                    return -1;
                if (memcmp(stored, block, sizeof(block))) {
                    fprintf(stderr, "FAIL: Merkle mismatch: level %" PRIu64
                            " block %" PRIu64 "\n", level, b);
                    errno = EBADMSG;
                    return -1;
                }
            }
        }
    }

    if (l->levels) {
        uint64_t top = l->pos[l->levels - 1];
        if (io_full(hash_fd, block, sizeof(block), top * BLOCK_SIZE, false))
            return -1;
    } else {
        if (io_full(data_fd, block, sizeof(block), 0, false))
            return -1;
    }
    return hash_one(hc, block, sizeof(block), root);
}

static int cmd_tree(bool create, const char *alg, const char *data_path,
                    const char *tree_path, const char *root_arg,
                    const char *salt_arg)
{
    unsigned char salt[MAX_SALT] = {0}, root[DIGEST_SIZE], trusted[DIGEST_SIZE];
    size_t salt_len = 0, trusted_len = 0;
    uint64_t bytes = 0, tree_bytes = 0;
    struct layout l;
    struct hash_ctx hc;
    int data_fd = -1, hash_fd = -1, result = 1;
    bool created = false;

    if (!supported(alg) || decode_hex(salt_arg, salt, sizeof(salt), &salt_len)) {
        fprintf(stderr, "Invalid algorithm or salt (hex, maximum 256 bytes).\n");
        return 2;
    }
    if (!create && (decode_hex(root_arg, trusted, sizeof(trusted),
                                &trusted_len) || trusted_len != DIGEST_SIZE)) {
        fprintf(stderr, "Trusted root must be exactly 32 bytes (64 hex chars).\n");
        return 2;
    }

    data_fd = open(data_path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW);
    if (data_fd < 0 || file_size(data_fd, &bytes)) {
        perror("Cannot inspect data file");
        goto out;
    }
    if (bytes == 0 || bytes % BLOCK_SIZE != 0 ||
        layout_init(&l, bytes / BLOCK_SIZE)) {
        fprintf(stderr, "Input must have a positive, 4096-aligned size.\n");
        goto out;
    }

    if (create) {
        // Never overwrite a device, symlink or existing hash tree.
        hash_fd = open(tree_path, O_RDWR | O_CREAT | O_EXCL | O_CLOEXEC, 0600);
        if (hash_fd >= 0) created = true;
    } else {
        hash_fd = open(tree_path, O_RDONLY | O_CLOEXEC | O_NOFOLLOW);
    }
    if (hash_fd < 0) {
        perror("Cannot open hash tree (format requires a NEW path)");
        goto out;
    }
    if (create) {
        // A single-data-block tree has no hash blocks, but requires a
        // nonempty regular backing file if later attached to a loop device.
        uint64_t alloc_blocks = l.hash_blocks ? l.hash_blocks : 1;
        if (ftruncate(hash_fd, (off_t)(alloc_blocks * BLOCK_SIZE))) {
            perror("Cannot size hash file");
            goto out;
        }
    } else {
        if (file_size(hash_fd, &tree_bytes) ||
            tree_bytes < l.hash_blocks * BLOCK_SIZE) {
            fprintf(stderr, "Hash tree is shorter than required.\n");
            goto out;
        }
    }
    if (open_hash(&hc, alg, salt, salt_len)) goto out;

    if (tree_process(&hc, data_fd, hash_fd, &l, create, root)) {
        perror("Merkle tree calculation");
        close(hc.tfm);
        goto out;
    }
    close(hc.tfm);

    if (!create && memcmp(root, trusted, DIGEST_SIZE)) {
        fprintf(stderr, "FAIL: computed root differs from trusted root.\n");
        goto out;
    }

    if (create && fsync(hash_fd)) {
        perror("fsync hash tree");
        goto out;
    }
    printf("%s: %s\n", create ? "FORMAT PASS" : "VERIFY PASS", alg);
    printf("Data blocks: %" PRIu64 "\n", l.data_blocks);
    printf("Hash blocks: %" PRIu64 "\n", l.hash_blocks);
    printf("Hash start block: 0\nSalt: ");
    if (salt_len) print_hex(salt, salt_len);
    else putchar('-');
    printf("\nRoot hash: ");
    print_hex(root, sizeof(root));
    putchar('\n');
    result = 0;
out:
    if (hash_fd >= 0) close(hash_fd);
    if (data_fd >= 0) close(data_fd);
    if (created && result) unlink(tree_path); // remove only our partial output
    return result;
}

static int cmd_table(const char *alg, const char *data, const char *hash,
                     const char *blocks_arg, const char *root_arg,
                     const char *salt_arg)
{
    unsigned char root[DIGEST_SIZE], salt[MAX_SALT];
    size_t rl = 0, sl = 0;
    uint64_t blocks;
    char *end;
    struct layout layout;

    if (!supported(alg) ||
        decode_hex(root_arg, root, sizeof(root), &rl) || rl != DIGEST_SIZE ||
        decode_hex(salt_arg, salt, sizeof(salt), &sl)) return 2;
    if (strncmp(data, "/dev/", 5) || strncmp(hash, "/dev/", 5) ||
        strpbrk(data, " \t\n\r") || strpbrk(hash, " \t\n\r")) {
        fprintf(stderr, "Table requires absolute /dev/ block device paths.\n");
        return 2;
    }
    errno = 0;
    blocks = strtoull(blocks_arg, &end, 10);
    if (errno || *end || layout_init(&layout, blocks)) return 2;
    // dmsetup expects: start_sector length_sectors verity version ...
    printf("0 %" PRIu64 " verity 1 %s %s %u %u %" PRIu64
           " 0 %s ", blocks * (BLOCK_SIZE / 512U), data, hash,
           BLOCK_SIZE, BLOCK_SIZE, blocks, alg);
    print_hex(root, sizeof(root));
    printf(" ");
    if (sl) print_hex(salt, sl);
    else putchar('-');
    putchar('\n');
    return 0;
}

static int cmd_selftest(const char *alg)
{
    struct hash_ctx hc;
    unsigned char data[8192], result[DIGEST_SIZE], expect[DIGEST_SIZE];
    const struct vector {
        size_t len;
        const char *hex;
    } vectors[] = {
        {0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262"},
        {1, "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e"},
        {63, "e9bc37a594daad83be9470df7f7b3798297c3d834ce80ba85d6e207627b7db7b"},
        {64, "4eed7141ea4a5cd4b788606bd23f46e212af9cacebacdc7d1f4c6dc7f2511b98"},
        {65, "de1e5fa0be70df6d2be8fffd0e99ceaa8eb6e8c93a63f2d8d1c30ecb6b263dee"},
        {1023, "10108970eeda3eb932baac1428c7a2163b0e924c9a9e25b35bba72b28f70bd11"},
        {1024, "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7"},
        {1025, "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444"},
        {2048, "e776b6028c7cd22a4d0ba182a8bf62205d2ef576467e838ed6f2529b85fba24a"},
        {4096, "015094013f57a5277b59d8475c0501042c0b642e531b0a1c8f58d2163229e969"},
        {8192, "aae792484c8efe4f19e2ca7d371d8c467ffb10748d8a5a1ae579948f718a2a63"}
    };
    size_t unused;
    if (strcmp(alg, "blake3") != 0) {
        fprintf(stderr, "Selftest vectors currently cover blake3 only.\n");
        return 2;
    }
    for (size_t i = 0; i < sizeof(data); i++)
        data[i] = (unsigned char)(i % 251U);
    if (open_hash(&hc, alg, NULL, 0)) return 1;
    for (size_t i = 0; i < sizeof(vectors) / sizeof(vectors[0]); i++) {
        if (decode_hex(vectors[i].hex, expect, sizeof(expect), &unused) ||
            hash_one(&hc, data, vectors[i].len, result)) {
            perror("BLAKE3 selftest execution");
            close(hc.tfm);
            return 1;
        }
        if (memcmp(expect, result, sizeof(result))) {
            fprintf(stderr, "SELFTEST FAIL length=%zu expected=", vectors[i].len);
            for (size_t j = 0; j < sizeof(result); j++)
                fprintf(stderr, "%02x", expect[j]);
            fprintf(stderr, " got=");
            for (size_t j = 0; j < sizeof(result); j++)
                fprintf(stderr, "%02x", result[j]);
            fprintf(stderr, "\n");
            close(hc.tfm);
            return 1;
        }
        printf("SELFTEST PASS: blake3 bytes=%zu\n", vectors[i].len);
    }
    close(hc.tfm);
    return 0;
}

int main(int argc, char **argv)
{
    const char *salt = "-";
    if (argc == 3 && strcmp(argv[1], "selftest") == 0)
        return cmd_selftest(argv[2]);
    if ((argc == 5 || argc == 6) && strcmp(argv[1], "format") == 0) {
        if (argc == 6) salt = argv[5];
        return cmd_tree(true, argv[2], argv[3], argv[4], NULL, salt);
    }
    if ((argc == 6 || argc == 7) && strcmp(argv[1], "verify") == 0) {
        if (argc == 7) salt = argv[6];
        return cmd_tree(false, argv[2], argv[3], argv[4], argv[5], salt);
    }
    if ((argc == 7 || argc == 8) && strcmp(argv[1], "table") == 0) {
        if (argc == 8) salt = argv[7];
        return cmd_table(argv[2], argv[3], argv[4], argv[5], argv[6], salt);
    }
    usage(argv[0]);
    return 2;
}
