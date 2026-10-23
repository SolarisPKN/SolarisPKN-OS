# SPDX-License-Identifier: MIT
"""SolarisPKN-Verity CLI. Never mounts/unlocks privileged devices."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from . import __version__, image

def root_payload(manifest):
    meta=image.load_manifest(Path(manifest))
    value=meta.get("root_hash")
    if not isinstance(value,str) or re.fullmatch("[0-9a-f]{64}",value) is None:
        raise ValueError("root_hash must be exactly 64 lowercase hex chars")
    return value.encode("ascii")

def cms(*args):
    if shutil.which("openssl") is None:
        raise RuntimeError("OpenSSL not available")
    p=subprocess.run(["openssl","cms",*map(str,args)],stdout=subprocess.PIPE,
                     stderr=subprocess.PIPE,check=False)
    if p.returncode:
        raise RuntimeError("OpenSSL CMS rejected operation (exit %d)" % p.returncode)

def sign(manifest,key,cert,output):
    key=Path(key);cert=Path(cert);output=Path(output);manifest=Path(manifest)
    if not key.is_file() or not cert.is_file():
        raise FileNotFoundError("certificate or private key missing")
    if output.exists() or output.is_symlink():
        raise FileExistsError("refusing to overwrite existing signature")
    if output.resolve() in {key.resolve(),cert.resolve(),manifest.resolve()}:
        raise ValueError("output conflicts with input")
    output.parent.mkdir(parents=True,exist_ok=True)
    old=os.umask(0o077)
    try:
        with tempfile.TemporaryDirectory(prefix=".spkn-cms-",dir=output.parent) as tmp:
            payload=Path(tmp)/"root-ascii"
            signature=Path(tmp)/"root.p7s"
            payload.write_bytes(root_payload(manifest))
            cms("-sign","-binary","-nosmimecap","-in",payload,"-signer",cert,
                "-inkey",key,"-md","sha256","-outform","DER","-out",signature)
            cms("-verify","-binary","-inform","DER","-in",signature,
                "-content",payload,"-CAfile",cert,"-purpose","any","-out",os.devnull)
            os.replace(signature,output)
            output.chmod(0o600)
    finally:
        os.umask(old)
    return {"status":"PASS","output":str(output),"payload":"ASCII hex root hash (64 bytes)",
            "format":"detached DER PKCS7 without SMIMECapabilities"}

def verify(manifest,signature,cert):
    if not Path(signature).is_file() or not Path(cert).is_file():
        raise FileNotFoundError("signature or certificate missing")
    with tempfile.TemporaryDirectory(prefix=".spkn-verify-") as tmp:
        payload=Path(tmp)/"root"
        payload.write_bytes(root_payload(manifest))
        cms("-verify","-binary","-inform","DER","-in",signature,
            "-content",payload,"-CAfile",cert,"-purpose","any","-out",os.devnull)
    return {"status":"PASS","certificate":"verified against specified CA file"}

def doctor():
    path=Path("/proc/crypto")
    matches=[]
    if path.exists():
        matches=[line for line in path.read_text(errors="replace").splitlines()
                 if line.startswith("name") and "blake3" in line.lower()]
    simd=Path("/sys/module/blake3_solarispkn/parameters/simd")
    return {"status":"INSPECTED","kernel":os.uname().release if hasattr(os,"uname") else "unknown",
            "blake3_registered":bool(matches),"crypto_entries":matches,
            "simd_mode":simd.read_text().strip() if simd.is_file() else "unavailable",
            "openssl":shutil.which("openssl") is not None,
            "note":"does not attest a UEFI Secure Boot chain"}

def main(argv=None):
    p=argparse.ArgumentParser(prog="solarispkn-verity",
        description="Build/verify Merkle roots and detached PKCS7; no privileged actions")
    p.add_argument("--version",action="version",version=__version__)
    sub=p.add_subparsers(dest="action",required=True)
    for cmd in ["build","verify","audit","table"]:
        sub.add_parser(cmd,add_help=False)
    signp=sub.add_parser("sign-root")
    for name in ["manifest","private-key","cert","output"]:
        signp.add_argument("--"+name,required=True,type=Path)
    ver=sub.add_parser("verify-signature")
    for name in ["manifest","signature","cert"]:
        ver.add_argument("--"+name,required=True,type=Path)
    sub.add_parser("doctor")
    args,extras=p.parse_known_args(argv)
    if args.action in ("build","verify","audit","table"):
        return image.main([args.action]+extras)
    if extras:
        p.error("Unknown option: "+" ".join(extras))
    try:
        if args.action=="sign-root":
            result=sign(args.manifest,args.private_key,args.cert,args.output)
        elif args.action=="verify-signature":
            result=verify(args.manifest,args.signature,args.cert)
        else:
            result=doctor()
        print(json.dumps(result,sort_keys=True))
        return 0
    except (ValueError,RuntimeError,FileNotFoundError,OSError) as exc:
        print("solarispkn-verity: "+str(exc),file=sys.stderr)
        return 1

if __name__=="__main__":
    raise SystemExit(main())
