# Yocto integration (scaffold)

The `meta-solarispkn-verity` layer currently contains **no buildable recipes**. It is a valid layer starting point targeting Yocto Scarthgap.

After initializing your Yocto build, register it using:

```sh
bitbake-layers add-layer /absolute/path/SolarisPKN-OS/solarispkn-verity/integrations/yocto/meta-solarispkn-verity
bitbake-layers show-layers
```

This step does **not** enable BLAKE3 or dm-verity. Later releases will include the kernel module recipe, user-space tree tooling and image integration. Existing upstream dm-verity remains the block-verification engine.
