ifneq ($(KERNELRELEASE),)
obj-m += blake3_solarispkn.o
blake3_solarispkn-y := spkn_blake3_shash.o blake3.o blake3_portable.o blake3_dispatch_kernel.o
ifeq ($(CONFIG_X86_64),y)
blake3_solarispkn-y += blake3_sse2_x86-64_unix.o blake3_sse41_x86-64_unix.o blake3_avx2_x86-64_unix.o blake3_avx512_x86-64_unix.o
endif
ccflags-y += -DBLAKE3_USE_NEON=0
else
SRC := $(shell pwd)
all:
	$(MAKE) -C $(KERNEL_SRC) M=$(SRC) modules
modules_install:
	$(MAKE) -C $(KERNEL_SRC) M=$(SRC) modules_install
clean:
	$(MAKE) -C $(KERNEL_SRC) M=$(SRC) clean
endif
