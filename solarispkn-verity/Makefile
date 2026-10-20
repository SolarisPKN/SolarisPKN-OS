# SPDX-License-Identifier: GPL-2.0-only
PACKAGE := solarispkn-verity
VERSION := $(shell sed -n '1p' VERSION)
DIST_DIR ?= dist
DIST_ABS := $(abspath $(DIST_DIR))
.PHONY: help check package clean

help:
	@printf 'SolarisPKN-Verity source packaging\n'
	@printf '  make check    validate package metadata\n'
	@printf '  make package  create a source scaffold tar.gz\n'
	@printf '  make clean    remove generated archives\n'

check:
	@test -s VERSION
	@test -s LICENSE
	@test -s README.md
	@test -s THIRD_PARTY.md
	@test -f integrations/yocto/meta-solarispkn-verity/conf/layer.conf
	@printf 'Metadata OK: %s %s (scaffold only)\n' '$(PACKAGE)' '$(VERSION)'

package: check
	@mkdir -p "$(DIST_DIR)"
	@tar -C .. --exclude='$(PACKAGE)/dist' --exclude='$(PACKAGE)/.git' -czf "$(DIST_ABS)/$(PACKAGE)-$(VERSION).tar.gz" "$(PACKAGE)"
	@printf 'Created %s/%s-%s.tar.gz\n' "$(DIST_DIR)" "$(PACKAGE)" "$(VERSION)"

clean:
	@rm -rf -- "$(DIST_DIR)"
