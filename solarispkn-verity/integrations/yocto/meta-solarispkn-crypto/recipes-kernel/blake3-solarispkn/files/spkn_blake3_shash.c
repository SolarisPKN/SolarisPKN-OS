// SPDX-License-Identifier: GPL-2.0-only
/*
 * SolarisPKN portable BLAKE3 Crypto API SHASH (research, NOT production audited).
 * Portable upstream BLAKE3 is CC0.
 *
 * Linux 6.18 bounds both descsize and statesize, whereas upstream BLAKE3 uses
 * a much larger chaining-value stack. Keep an inline <=64-byte prefix, then
 * allocate complete BLAKE3 state dynamically.
 *
 * Owner registry is per crypto_shash transform, not global, and keyed by the
 * descriptor address. init/import never dereference an uninitialized pointer.
 * Re-init, import, digest and tfm destruction safely reclaim tracked states.
 * Abandoned descriptors are reclaimed when the owning tfm is freed.
 * Full export after spilling the prefix remains -EOPNOTSUPP.
 */
#include <crypto/internal/hash.h>
#include <linux/errno.h>
#include <linux/init.h>
#include <linux/list.h>
#include <linux/module.h>
#include <linux/slab.h>
#include <linux/spinlock.h>
#include <linux/string.h>
#include "blake3.h"

/* SHASH owner lifetime has no descriptor destructor. Keep an explicit
 * per-tfm allocation ceiling until the full ownership design is reviewed.
 * This is a prototype safeguard, not a final release memory guarantee. */
#define SPKN_B3_MAX_ACTIVE_ALLOCS 512U
#define SPKN_B3_PREFIX_BYTES 64U
#define SPKN_B3_SNAPSHOT_MAGIC 0x53425033U

struct spkn_b3_allocation {
	struct list_head link;
	struct shash_desc *owner;
	blake3_hasher hasher;
};

struct spkn_b3_tfm {
	spinlock_t lock;
	struct list_head allocations;
	unsigned int live_allocations;
};

struct spkn_b3_desc {
	struct spkn_b3_allocation *state;
	u32 used;
	u8 prefix[SPKN_B3_PREFIX_BYTES];
};

struct spkn_b3_snapshot {
	u32 magic;
	u32 used;
	u8 prefix[SPKN_B3_PREFIX_BYTES];
};

/* Never trust desc->state before crypto_shash_init(): the descriptor may be
 * uninitialized on its first use, including single-shot crypto_shash_digest().
 * The per-tfm registry is the sole authority for allocation ownership.
 */
static void spkn_b3_recycle(struct shash_desc *d)
{
	struct spkn_b3_tfm *tfm = crypto_shash_ctx(d->tfm);
	struct spkn_b3_desc *ctx = shash_desc_ctx(d);
	struct spkn_b3_allocation *entry, *next, *detached = NULL;
	unsigned long flags;

	spin_lock_irqsave(&tfm->lock, flags);
	list_for_each_entry_safe(entry, next, &tfm->allocations, link) {
		if (entry->owner == d) {
			list_del_init(&entry->link);
			if (WARN_ON_ONCE(!tfm->live_allocations))
				tfm->live_allocations = 0;
			else
				tfm->live_allocations--;
			detached = entry;
			break;
		}
	}
	spin_unlock_irqrestore(&tfm->lock, flags);

	if (detached)
		kfree_sensitive(detached);
	ctx->state = NULL;
	ctx->used = 0;
	memzero_explicit(ctx->prefix, sizeof(ctx->prefix));
}

static int spkn_b3_init(struct shash_desc *d)
{
	spkn_b3_recycle(d);
	return 0;
}

static int spkn_b3_update(struct shash_desc *d, const u8 *data,
			  unsigned int len)
{
	struct spkn_b3_desc *ctx = shash_desc_ctx(d);
	struct spkn_b3_allocation *node;
	struct spkn_b3_tfm *tfm;
	unsigned long flags;

	if (!len)
		return 0;
	if (unlikely(!data))
		return -EINVAL;

	if (ctx->state) {
		blake3_hasher_update(&ctx->state->hasher, data, len);
		return 0;
	}
	if (unlikely(ctx->used > SPKN_B3_PREFIX_BYTES))
		return -EINVAL;
	if (len <= SPKN_B3_PREFIX_BYTES - ctx->used) {
		memcpy(ctx->prefix + ctx->used, data, len);
		ctx->used += len;
		return 0;
	}

	node = kmalloc(sizeof(*node), GFP_ATOMIC);
	if (!node)
		return -ENOMEM;
	node->owner = d;
	blake3_hasher_init(&node->hasher);
	blake3_hasher_update(&node->hasher, ctx->prefix, ctx->used);
	blake3_hasher_update(&node->hasher, data, len);

	tfm = crypto_shash_ctx(d->tfm);
	spin_lock_irqsave(&tfm->lock, flags);
	if (unlikely(tfm->live_allocations >= SPKN_B3_MAX_ACTIVE_ALLOCS)) {
		spin_unlock_irqrestore(&tfm->lock, flags);
		kfree_sensitive(node);
		return -ENOMEM;
	}
	list_add(&node->link, &tfm->allocations);
	tfm->live_allocations++;
	spin_unlock_irqrestore(&tfm->lock, flags);
	ctx->state = node;
	memzero_explicit(ctx->prefix, sizeof(ctx->prefix));
	ctx->used = 0;
	return 0;
}

static int spkn_b3_final(struct shash_desc *d, u8 *out)
{
	struct spkn_b3_desc *ctx = shash_desc_ctx(d);
	blake3_hasher *tmp;

	if (ctx->state) {
		blake3_hasher_finalize(&ctx->state->hasher, out, BLAKE3_OUT_LEN);
		spkn_b3_recycle(d);
		return 0;
	}
	tmp = kmalloc(sizeof(*tmp), GFP_ATOMIC);
	if (!tmp)
		return -ENOMEM;
	blake3_hasher_init(tmp);
	blake3_hasher_update(tmp, ctx->prefix, ctx->used);
	blake3_hasher_finalize(tmp, out, BLAKE3_OUT_LEN);
	kfree_sensitive(tmp);
	ctx->used = 0;
	memzero_explicit(ctx->prefix, sizeof(ctx->prefix));
	return 0;
}

static int spkn_b3_digest(struct shash_desc *d, const u8 *data,
			  unsigned int len, u8 *out)
{
	int err = spkn_b3_init(d);

	if (err)
		return err;
	err = spkn_b3_update(d, data, len);
	if (err)
		return err;
	return spkn_b3_final(d, out);
}

static int spkn_b3_export(struct shash_desc *d, void *out)
{
	struct spkn_b3_desc *ctx = shash_desc_ctx(d);
	struct spkn_b3_snapshot *snap = out;

	if (ctx->state)
		return -EOPNOTSUPP;
	snap->magic = SPKN_B3_SNAPSHOT_MAGIC;
	snap->used = ctx->used;
	memcpy(snap->prefix, ctx->prefix, sizeof(snap->prefix));
	return 0;
}

static int spkn_b3_import(struct shash_desc *d, const void *in)
{
	const struct spkn_b3_snapshot *snap = in;
	struct spkn_b3_desc *ctx = shash_desc_ctx(d);

	if (snap->magic != SPKN_B3_SNAPSHOT_MAGIC ||
	    snap->used > SPKN_B3_PREFIX_BYTES)
		return -EINVAL;
	spkn_b3_recycle(d);
	ctx->used = snap->used;
	memcpy(ctx->prefix, snap->prefix, sizeof(ctx->prefix));
	return 0;
}

static int spkn_b3_init_tfm(struct crypto_shash *tfm)
{
	struct spkn_b3_tfm *ctx = crypto_shash_ctx(tfm);

	spin_lock_init(&ctx->lock);
	INIT_LIST_HEAD(&ctx->allocations);
	ctx->live_allocations = 0;
	return 0;
}

static void spkn_b3_exit_tfm(struct crypto_shash *tfm)
{
	struct spkn_b3_tfm *ctx = crypto_shash_ctx(tfm);
	struct spkn_b3_allocation *entry, *next;
	LIST_HEAD(orphaned);
	unsigned long flags;

	spin_lock_irqsave(&ctx->lock, flags);
	list_splice_init(&ctx->allocations, &orphaned);
	ctx->live_allocations = 0;
	spin_unlock_irqrestore(&ctx->lock, flags);
	list_for_each_entry_safe(entry, next, &orphaned, link) {
		list_del(&entry->link);
		kfree_sensitive(entry);
	}
}

static struct shash_alg spkn_b3_alg = {
	.digestsize = BLAKE3_OUT_LEN,
	.init = spkn_b3_init,
	.update = spkn_b3_update,
	.final = spkn_b3_final,
	.digest = spkn_b3_digest,
	.export = spkn_b3_export,
	.import = spkn_b3_import,
	.init_tfm = spkn_b3_init_tfm,
	.exit_tfm = spkn_b3_exit_tfm,
	.statesize = sizeof(struct spkn_b3_snapshot),
	.descsize = sizeof(struct spkn_b3_desc),
	.base = {
		.cra_name = "blake3",
		.cra_driver_name = "blake3-solarispkn-portable",
		.cra_priority = 100,
		.cra_blocksize = BLAKE3_BLOCK_LEN,
		.cra_ctxsize = sizeof(struct spkn_b3_tfm),
		.cra_flags = CRYPTO_ALG_ALLOCATES_MEMORY,
		.cra_module = THIS_MODULE,
	}
};

static int __init spkn_b3_register(void)
{
	return crypto_register_shash(&spkn_b3_alg);
}

static void __exit spkn_b3_unregister(void)
{
	crypto_unregister_shash(&spkn_b3_alg);
}

module_init(spkn_b3_register);
module_exit(spkn_b3_unregister);
MODULE_LICENSE("GPL");
MODULE_DESCRIPTION("SolarisPKN BLAKE3 shash with tfm-owned dynamic states (lab)");
MODULE_AUTHOR("SolarisPKN");
MODULE_ALIAS_CRYPTO("blake3");
MODULE_ALIAS_CRYPTO("blake3-solarispkn-portable");
