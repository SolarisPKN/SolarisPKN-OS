# SPDX-License-Identifier: GPL-2.0-only
CC ?= cc
CFLAGS ?= -O2 -g
CPPFLAGS ?=
LDFLAGS ?=
CFLAGS += -std=c11 -Wall -Wextra -Wpedantic
TARGET := solarispkn-verity-tree

.PHONY: all clean selftest
all: $(TARGET)

$(TARGET): verity-tree.c
	$(CC) $(CPPFLAGS) $(CFLAGS) -o $@ $< $(LDFLAGS)

selftest: $(TARGET)
	./$(TARGET) selftest blake3

clean:
	rm -f -- $(TARGET)
