# 📦 SolarisPKN-Paste — signed APT repository

This is a **third-party SolarisPKN APT repository**, not an official Debian/Ubuntu archive. The Debian/Ubuntu .deb is the Linux CLI; no Windows GUI.

## Fastest setup (no GPG commands)

Download [solarispkn-repository_1.0-1_all.deb](https://github.com/SolarisPKN/SolarisPKN-OS/raw/refs/heads/main/apt/bootstrap/solarispkn-repository_1.0-1_all.deb) from the project, then run from the download directory:

~~~bash
sudo apt install ./solarispkn-repository_1.0-1_all.deb
sudo apt update && sudo apt install solarispkn-paste
~~~

This small, script-free .deb includes a dedicated public APT key and a scoped Signed-By source definition. No manual key commands or edits to sources.list are needed. See [easy onboarding documentation](../../packaging/apt/README.md). If you previously used the older shell bootstrap, do not keep both repository entries; check migration notes first.

## Install on Debian or Ubuntu (manual advanced alternative)

The repository is signed with a dedicated OpenPGP Ed25519 key. Once the bootstrap is available in SolarisPKN-OS main, review and execute:

~~~bash
curl -fL https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/solarispkn-paste/linux/packaging/bootstrap-apt.sh -o bootstrap-solarispkn-paste.sh
less bootstrap-solarispkn-paste.sh
sudo bash bootstrap-solarispkn-paste.sh
sudo apt install solarispkn-paste
solarispkn-paste --version
~~~

Later upgrades use the regular APT update procedure:

~~~bash
sudo apt update
sudo apt upgrade solarispkn-paste
~~~

The bootstrap installs a public key into /etc/apt/keyrings/solarispkn-paste.asc and a deb822 file into /etc/apt/sources.list.d/solarispkn-paste.sources with a dedicated Signed-By. It verifies the public key with a pinned SHA-256 value before trusting it. It does **not** use apt-key, trusted=yes, or disable signature validation.

**Public key fingerprint:** 4354 DE17 EC84 DE9B DFC3 23C3 8C5E 6F58 AD1B 01DA

**Public key file SHA-256:** 161e51d5f57c34ed48425bee201c4d13bab9a0383718230898a49ef68ad67181

The signed repository is served from:
https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/apt

It provides signed InRelease and Release.gpg, package metadata for amd64 and arm64, and an architecture-independent .deb. The repository key is **dedicated to this archive** and stored privately outside GitHub. At every package update the maintainer must rebuild indexes and re-sign Release before publishing; GitHub Actions intentionally does not have the private key.

## Remove third-party source

~~~bash
sudo rm /etc/apt/sources.list.d/solarispkn-paste.sources
sudo rm /etc/apt/keyrings/solarispkn-paste.asc
sudo apt update
~~~

To also uninstall the application: sudo apt remove solarispkn-paste. Personal files and recovery backups are not automatically removed.

## Offline verification and release QA

The signed index was accepted by APT running under Debian 13 in a temporary source/list directory **without modifying system APT settings**. APT reported Candidate: 0.3.0~alpha1-1. Smoke script:

~~~bash
bash solarispkn-paste/linux/tests/apt-signature-smoke.sh
~~~

Debian 13 and Ubuntu previously passed CI on the native CLI and installation. This test validates the **cryptographic repository metadata**; it does not mean the repository is registered by default on Debian/Ubuntu.

The signer key needs a secure offline backup and rotation before expiration. Do not commit the private key or a signing passphrase to the public repository.
