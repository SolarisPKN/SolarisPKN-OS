# One-click SolarisPKN repository setup for Debian/Ubuntu

**No manual GPG or sources.list commands.** SolarisPKN's third-party
repository is not the Debian/Ubuntu official archive. Package
solarispkn-repository is an optional, separate, architecture-independent
bootstrap package. It contains ONLY:

- /usr/share/keyrings/solarispkn-archive.asc (public archive signing key)
- /etc/apt/sources.list.d/solarispkn.sources (repository scoped via Signed-By)

It has no maintainer scripts, privileged services, apt-key calls, or global
trust configuration. The key fingerprint is
4354 DE17 EC84 DE9B DFC3 23C3 8C5E 6F58 AD1B 01DA.

## Quickest way: a single script or a graphical .deb download

- **[Download SolarisPKN-Paste .deb (graphical install)](https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/apt/pool/main/s/solarispkn-paste/solarispkn-paste_0.3.0~alpha1-1_all.deb)**: download and open in a GUI package installer when supported. This does not configure the optional third-party APT repository.
- **[Download the all-in-one installer script](https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/instalar-solarispkn-paste.sh)**: save the script and run `bash instalar-solarispkn-paste.sh`. It configures a scoped, signed third-party APT repo and installs Paste, so future updates arrive through APT.

One-line download + execution, without piping a remote response into a root shell:

```bash
curl -fsSLo instalar-solarispkn-paste.sh https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/instalar-solarispkn-paste.sh && bash instalar-solarispkn-paste.sh
```

The script validates the bootstrap .deb against pinned SHA-256 `eb20a79a0407e1178f4ad21eb1dff4c2f00ff1a09b77b517707d00384162905d`, runs `apt-get update` and installs `solarispkn-paste`. It does not run a blanket system upgrade or disable APT signature checks. It supports Debian/Ubuntu amd64 and arm64.

Clicking a GitHub download link cannot execute code on a visitor's computer. The user approves installation locally using the GUI installer or terminal.

## Simple installation

Download solarispkn-repository_1.0-1_all.deb from:
https://github.com/SolarisPKN/SolarisPKN-OS/raw/refs/heads/main/apt/bootstrap/solarispkn-repository_1.0-1_all.deb

Then in the downloads directory:

~~~sh
sudo apt install ./solarispkn-repository_1.0-1_all.deb
sudo apt update
sudo apt install solarispkn-paste
~~~

The third-party repository becomes available to normal apt upgrades,
and this configuration also supports future packages (such as
solarispkn-verity once its repository binary release exists).
The package does NOT auto-install SolarisPKN-Paste: you choose it.

Alternatively, download the Paste .deb directly from
https://github.com/SolarisPKN/SolarisPKN-OS/raw/refs/heads/main/apt/pool/main/s/solarispkn-paste/solarispkn-paste_0.3.0~alpha1-1_all.deb
and install with one command after download:

~~~sh
sudo apt install ./solarispkn-paste_0.3.0~alpha1-1_all.deb
~~~

That installs the tool without adding ANY third-party repository, but
does not automatically obtain future SolarisPKN-Paste releases.

## Trust and compatibility

Verify the downloaded bootstrap file's published SHA-256 on its release
page, especially on shared or managed servers. A locally downloaded .deb
does NOT become automatically authenticated by a distro's signed APT
repository. Once the source is installed, APT verifies the SolarisPKN
InRelease signature through the dedicated key and all package hashes.
The key cannot authorize unrelated Debian or Ubuntu repositories.

If you already installed the old solarispkn-paste.sources via a
bootstrap shell script, remove/migrate the duplicate definition first.
Do not keep two definitions for the same URI with different Signed-By
key paths: APT may reject both. This package will not silently delete
or rewrite that existing file.

To remove the third-party repository:

~~~sh
sudo apt purge solarispkn-repository
sudo apt update
~~~

Removing the repository does not automatically remove previously
installed SolarisPKN applications. They can be removed with apt remove.

## For official one-command installation

Only acceptance into Debian/Ubuntu official repositories (like the
Debian/Ubuntu docker.io package) enables "sudo apt install
solarispkn-paste" on a completely unmodified installation with no prior
repository setup. This dedicated repo installer is a temporary,
low-friction alternative, not an equivalent to official distro inclusion.
