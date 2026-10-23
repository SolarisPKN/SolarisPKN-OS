# SPDX-License-Identifier: GPL-2.0-only
PACKAGE := solarispkn-verity
VERSION := $(shell sed -n '1p' VERSION)
DIST_DIR ?= dist
DIST_ABS := $(abspath $(DIST_DIR))
.PHONY: help check package clean

help:
	@printf 'SolarisPKN-Verity source packaging\n'
	@printf '  make check    validate package metadata\n'
	@printf '  make package  create an experimental source-only tar.gz\n'
	@printf '  make clean    remove generated archives\n'

check:
	@test -s VERSION
	@test -s LICENSE
	@test -s README.md
	@test -s THIRD_PARTY.md
	@test -f integrations/yocto/meta-solarispkn-verity/conf/layer.conf
	@test -s integrations/yocto/meta-solarispkn-crypto/recipes-kernel/blake3-solarispkn/blake3-solarispkn_0.1.bb
	@test -s cli/pyproject.toml
	@printf 'Metadata OK: %s %s (experimental source)\n' '$(PACKAGE)' '$(VERSION)'

package: check
	@mkdir -p "$(DIST_DIR)"
	@tar -C .. --exclude='$(PACKAGE)/dist' --exclude='$(PACKAGE)/.git' -czf "$(DIST_ABS)/$(PACKAGE)-$(VERSION).tar.gz" "$(PACKAGE)"
	@printf 'Created %s/%s-%s.tar.gz\n' "$(DIST_DIR)" "$(PACKAGE)" "$(VERSION)"

clean:
	@rm -rf -- "$(DIST_DIR)"

# Build and validate the local experimental Linux userspace Merkle utility.
# The package target above continues to make a SOURCE archive only.
.PHONY: tools tool-selftest
tools:
	$(MAKE) -C tools/verity-tree

tool-selftest:
	$(MAKE) -C tools/verity-tree selftest

# Validate the new source/tool recipe without compiling anything.
check-source:
	@test -s tools/verity-tree/verity-tree.c
	@test -s tools/verity-tree/Makefile
	@test -s integrations/yocto/meta-solarispkn-verity/recipes-solarispkn/verity-tree/solarispkn-verity-tree_0.1.bb
	@test -s tests/test-verity-tree.sh
	@test -s tests/test-sha256-cryptsetup-compat.sh
	@printf 'Experimental Verity tool sources are present (NOT executable validation).\n'
.PHONY: check-source

# Non-root sanity checks on any Linux host with AF_ALG SHA-256 support.
# Does not claim BLAKE3, dm-verity block mapping, or Secure Boot validation.
.PHONY: sha256-smoke
sha256-smoke: tools
	sh tests/test-sha256-unprivileged.sh
