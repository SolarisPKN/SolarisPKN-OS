// SPDX-License-Identifier: GPL-2.0-only
/*
 * SolarisPKN BLAKE3 dispatch for Linux Crypto API.
 * Portable primitives are mandatory fallback on every architecture.
 * On x86_64 optimized upstream CC0 assembly is used ONLY when irq_fpu_usable()
 * and the Linux CPU feature masks permit it. Never use userspace CPUID and
 * never execute FPU code in atomic/unsafe contexts.
 */
#include "spkn_kernel_compat.h"
#include "blake3_impl.h"
#include <linux/module.h>
#include <linux/string.h>

static char *spkn_b3_simd_mode = "auto";
module_param_named(simd, spkn_b3_simd_mode, charp, 0444);
MODULE_PARM_DESC(simd, "BLAKE3 SIMD: auto, portable, avx512 (explicit opt-in)");

#if defined(CONFIG_X86_64)
#include <asm/fpu/api.h>
#include <asm/cpufeature.h>

static __always_inline bool spkn_b3_simd_safe(void)
{
	/* FPU availability depends on current IRQ/softirq/scheduler context. */
	return !strcmp(spkn_b3_simd_mode, "auto") && irq_fpu_usable();
}
static __always_inline bool spkn_b3_sse2(void)
{
	return spkn_b3_simd_safe() &&
	       cpu_feature_enabled(X86_FEATURE_XMM2);
}
static __always_inline bool spkn_b3_sse41(void)
{
	return spkn_b3_simd_safe() &&
	       cpu_feature_enabled(X86_FEATURE_XMM4_1);
}
static __always_inline bool spkn_b3_avx2(void)
{
	/* Kernel cpufeatures already mask disabled OS-managed XSAVE features. */
	return spkn_b3_simd_safe() &&
	       cpu_feature_enabled(X86_FEATURE_AVX2);
}
/* AVX-512 is opt-in, never selected by auto. OS xstate must be available. */
static __always_inline bool spkn_b3_avx512(void)
{
	return !strcmp(spkn_b3_simd_mode, "avx512") && irq_fpu_usable() &&
	       cpu_feature_enabled(X86_FEATURE_AVX512F) &&
	       cpu_feature_enabled(X86_FEATURE_AVX512DQ) &&
	       cpu_feature_enabled(X86_FEATURE_AVX512BW) &&
	       cpu_feature_enabled(X86_FEATURE_AVX512VL);
}
#endif

void blake3_compress_in_place(uint32_t cv[8],
    const uint8_t block[BLAKE3_BLOCK_LEN], uint8_t block_len,
    uint64_t counter, uint8_t flags)
{
#if defined(CONFIG_X86_64)
	if (spkn_b3_avx512()) {
		kernel_fpu_begin();
		blake3_compress_in_place_avx512(cv, block, block_len, counter, flags);
		kernel_fpu_end();
		pr_info_once("SPKN_BLAKE3_AVX512_COMPRESS_PATH\n");
		return;
	}
	if (spkn_b3_sse41()) {
		kernel_fpu_begin();
		blake3_compress_in_place_sse41(cv, block, block_len, counter, flags);
		kernel_fpu_end();
		return;
	}
	if (spkn_b3_sse2()) {
		kernel_fpu_begin();
		blake3_compress_in_place_sse2(cv, block, block_len, counter, flags);
		kernel_fpu_end();
		return;
	}
#endif
	blake3_compress_in_place_portable(cv, block, block_len, counter, flags);
}

void blake3_compress_xof(const uint32_t cv[8],
    const uint8_t block[BLAKE3_BLOCK_LEN], uint8_t block_len,
    uint64_t counter, uint8_t flags, uint8_t out[64])
{
#if defined(CONFIG_X86_64)
	if (spkn_b3_avx512()) {
		kernel_fpu_begin();
		blake3_compress_xof_avx512(cv, block, block_len, counter, flags, out);
		kernel_fpu_end();
		pr_info_once("SPKN_BLAKE3_AVX512_XOF_PATH\n");
		return;
	}
	if (spkn_b3_sse41()) {
		kernel_fpu_begin();
		blake3_compress_xof_sse41(cv, block, block_len, counter, flags, out);
		kernel_fpu_end();
		return;
	}
	if (spkn_b3_sse2()) {
		kernel_fpu_begin();
		blake3_compress_xof_sse2(cv, block, block_len, counter, flags, out);
		kernel_fpu_end();
		return;
	}
#endif
	blake3_compress_xof_portable(cv, block, block_len, counter, flags, out);
}

void blake3_xof_many(const uint32_t cv[8], const uint8_t block[BLAKE3_BLOCK_LEN],
    uint8_t block_len, uint64_t counter, uint8_t flags,
    uint8_t out[64], size_t outblocks)
{
	size_t i;

	for (i = 0; i < outblocks; i++)
		blake3_compress_xof(cv, block, block_len, counter + i, flags,
				   out + i * 64);
}

void blake3_hash_many(const uint8_t *const *inputs, size_t num_inputs,
    size_t blocks, const uint32_t key[8], uint64_t counter,
    bool increment_counter, uint8_t flags, uint8_t flags_start,
    uint8_t flags_end, uint8_t *out)
{
#if defined(CONFIG_X86_64)
	if (spkn_b3_avx512()) {
		kernel_fpu_begin();
		blake3_hash_many_avx512(inputs, num_inputs, blocks, key,
		    counter, increment_counter, flags, flags_start, flags_end, out);
		kernel_fpu_end();
		pr_info_once("SPKN_BLAKE3_AVX512_HASH_MANY_PATH\n");
		return;
	}
	if (spkn_b3_avx2()) {
		kernel_fpu_begin();
		blake3_hash_many_avx2(inputs, num_inputs, blocks, key,
		    counter, increment_counter, flags, flags_start, flags_end, out);
		kernel_fpu_end();
		return;
	}
	if (spkn_b3_sse41()) {
		kernel_fpu_begin();
		blake3_hash_many_sse41(inputs, num_inputs, blocks, key,
		    counter, increment_counter, flags, flags_start, flags_end, out);
		kernel_fpu_end();
		return;
	}
	if (spkn_b3_sse2()) {
		kernel_fpu_begin();
		blake3_hash_many_sse2(inputs, num_inputs, blocks, key,
		    counter, increment_counter, flags, flags_start, flags_end, out);
		kernel_fpu_end();
		return;
	}
#endif
	blake3_hash_many_portable(inputs, num_inputs, blocks, key, counter,
		increment_counter, flags, flags_start, flags_end, out);
}

size_t blake3_simd_degree(void)
{
#if defined(CONFIG_X86_64)
	if (spkn_b3_avx512())
		return 16;
	if (spkn_b3_avx2())
		return 8;
	if (spkn_b3_sse41() || spkn_b3_sse2())
		return 4;
#endif
	return 1;
}
