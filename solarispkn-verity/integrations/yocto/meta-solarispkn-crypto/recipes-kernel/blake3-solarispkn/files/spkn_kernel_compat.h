/* SPDX-License-Identifier: GPL-2.0-only */
#ifndef SPKN_KERNEL_COMPAT_H
#define SPKN_KERNEL_COMPAT_H
#include <linux/types.h>
#include <linux/stddef.h>
#include <linux/string.h>
#include <linux/bug.h>
#include <linux/bitops.h>
typedef u8 uint8_t;
typedef u32 uint32_t;
typedef u64 uint64_t;
#define assert(condition) WARN_ON_ONCE(!(condition))
#endif
