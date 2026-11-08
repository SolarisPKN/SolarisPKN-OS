# SPDX-License-Identifier: GPL-3.0-only
"""SolarisPKN-Paste for Linux: conservative I/O profiling and verified atomic copies."""
from __future__ import annotations

import argparse
import concurrent.futures
import dataclasses
import datetime as dt
import errno
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import time
import uuid

VERSION = "0.3.0-alpha.1"
MiB = 1024 * 1024
GiB = 1024 * MiB


class CopyError(Exception):
    pass


def media_profile(path: Path) -> dict:
    """Find Linux block device and follow sysfs RAID/LVM leaves; uncertainty is explicit."""
    existing = path
    while not existing.exists() and existing != existing.parent:
        existing = existing.parent
    try:
        device = existing.stat().st_dev
        major_minor = f"{os.major(device)}:{os.minor(device)}"
        node = Path("/sys/dev/block") / major_minor
        if not node.exists():
            return {"media": "unknown", "reason": "virtual/non-block mount", "device": major_minor}
        resolved = node.resolve()
        seen: set[str] = set()
        leaves: list[dict] = []

        def walk(location: Path, depth: int = 0) -> None:
            if depth > 12 or str(location) in seen:
                return
            seen.add(str(location))
            parent = location
            while not (parent / "slaves").exists() and parent != parent.parent:
                parent = parent.parent
            slaves = parent / "slaves"
            children = list(slaves.iterdir()) if slaves.exists() else []
            if children:
                for child in children:
                    walk(child.resolve(), depth + 1)
                return
            disk = parent
            while not (disk / "queue" / "rotational").exists() and disk != disk.parent:
                disk = disk.parent
            rota_file = disk / "queue" / "rotational"
            rotational = rota_file.read_text().strip() if rota_file.exists() else "?"
            name = disk.name
            transport = "unknown"
            try:
                transport = (disk / "device" / "model").read_text().strip()
            except (OSError, UnicodeError):
                pass
            leaves.append({"name": name, "rotational": rotational, "model": transport})

        walk(resolved)
        if "microsoft" in os.uname().release.lower() or "wsl" in os.uname().release.lower():
            media = "unknown"  # WSL2 synthetic block media does not identify its host drive.
            reason = "WSL virtual device: host HDD/SSD not verifiable"
        elif not leaves or any(x["rotational"] not in {"0", "1"} for x in leaves):
            media, reason = "unknown", "some RAID/LVM member properties unavailable"
        elif any(x["rotational"] == "1" for x in leaves):
            media, reason = "hdd", "mechanical member detected"
        else:
            media, reason = "ssd", "all leaf devices non-rotational"
        return {"media": media, "reason": reason, "device": major_minor,
                "members": leaves, "filesystem": fs_type(existing)}
    except (OSError, ValueError, PermissionError) as error:
        return {"media": "unknown", "reason": type(error).__name__}


def fs_type(path: Path) -> str:
    try:
        device = path.stat().st_dev
        candidate = ""
        for row in Path("/proc/self/mountinfo").read_text().splitlines():
            left, sep, right = row.partition(" - ")
            columns = left.split()
            if len(columns) < 5 or len(right.split()) < 1:
                continue
            if columns[2] == f"{os.major(device)}:{os.minor(device)}":
                candidate = right.split()[0]
        return candidate or "unknown"
    except (OSError, ValueError):
        return "unknown"


def cpu_profile() -> dict:
    ram = 0
    try:
        for line in Path("/proc/meminfo").read_text().splitlines():
            if line.startswith("MemTotal:"):
                ram = int(line.split()[1]) * 1024
                break
    except (OSError, ValueError):
        pass
    return {"logical_cpus": os.cpu_count() or 1, "ram_bytes": ram,
            "kernel": os.uname().release, "python": sys.version.split()[0]}


def strategy(source: Path, destination: Path) -> dict:
    inp, out = media_profile(source), media_profile(destination)
    types = {inp["media"], out["media"]}
    if "hdd" in types:
        mode, workers = "mechanical-queue", 1
    elif "unknown" in types:
        mode, workers = "conservative-queue", 1
    else:
        host = cpu_profile()
        workers = min(4, max(1, host["logical_cpus"] // 2))
        if host["logical_cpus"] <= 2 or host["ram_bytes"] < 4 * GiB:
            workers = 1
        mode = "solid-parallel" if workers > 1 else "solid-single"
    return {"mode": mode, "workers": workers, "source": inp, "destination": out}


def digest(path: Path, algorithm: str) -> str:
    if algorithm == "blake3":
        executable = shutil.which("b3sum")
        if executable is None:
            raise CopyError("BLAKE3 requires 'b3sum' (install with apt install b3sum). "
                            "To explicitly use SHA-256 instead, pass --hash sha256.")
        result = subprocess.run([executable, "--", str(path)],
                                capture_output=True, text=True, check=False)
        if result.returncode != 0:
            raise CopyError("b3sum failed: " + result.stderr.strip()[:300])
        result_digest = result.stdout[:64].lower()
        if not re.fullmatch(r"[0-9a-f]{64}", result_digest):
            raise CopyError("Invalid BLAKE3 digest output")
        return result_digest
    if algorithm != "sha256":
        raise CopyError("Unsupported digest")
    ctx = hashlib.sha256()
    with path.open("rb", buffering=0) as stream:
        while True:
            block = stream.read(4 * MiB)
            if not block:
                break
            ctx.update(block)
    return ctx.hexdigest()


def reject_link(path: Path, label: str) -> None:
    if path.is_symlink():
        raise CopyError(f"Refusing symbolic link ({label}): {path}")


def ensure_parent(path: Path) -> None:
    for item in [path.parent, *path.parent.parents]:
        if item.exists():
            reject_link(item, "parent")
            if not item.is_dir():
                raise CopyError(f"Not a directory: {item}")
    path.parent.mkdir(parents=True, exist_ok=True)


def fsync_dir(path: Path) -> None:
    try:
        fd = os.open(path, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
    except OSError:
        return
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def copy_bytes(source: Path, target: Path, engine: str) -> str:
    # Buffered Python and kernel copy_file_range are independent backends.
    with source.open("rb", buffering=0) as incoming, target.open("xb", buffering=0) as outgoing:
        def buffered() -> None:
            while True:
                block = incoming.read(1 * MiB)
                if not block:
                    break
                outgoing.write(block)

        used = "buffered"
        if engine == "copyfile-range" and hasattr(os, "copy_file_range"):
            try:
                remaining = source.stat().st_size
                while remaining > 0:
                    copied = os.copy_file_range(incoming.fileno(), outgoing.fileno(),
                                                min(4 * MiB, remaining))
                    if copied == 0:
                        raise OSError(errno.EIO, "Premature EOF")
                    remaining -= copied
                used = "copyfile-range"
            except OSError as ex:
                if ex.errno not in (errno.EXDEV, errno.ENOSYS, errno.EINVAL,
                                    errno.EOPNOTSUPP):
                    raise
                # fallback from scratch, never append to incomplete kernel copy
                incoming.seek(0)
                outgoing.truncate(0)
                outgoing.seek(0)
                buffered()
                used = "buffered-fallback"
        else:
            buffered()
        outgoing.flush()
        os.fsync(outgoing.fileno())
    return used


def file_task(source: Path, target: Path, algorithm: str,
              engine: str, overwrite: bool, backup_root: Path | None) -> dict:
    reject_link(source, "source")
    if not source.is_file():
        raise CopyError(f"Not a regular file: {source}")
    before = source.stat()
    if not stat.S_ISREG(before.st_mode):
        raise CopyError(f"Not a regular file: {source}")
    ensure_parent(target)
    reject_link(target, "destination")
    if target.exists() and not overwrite:
        raise CopyError(f"Target exists (use --overwrite): {target}")
    if target.exists() and not target.is_file():
        raise CopyError(f"Target is not a regular file: {target}")
    # A staging file always resides on the same target filesystem.
    stage = target.with_name(f".{target.name}.spaste-{uuid.uuid4().hex}.tmp")
    started = time.monotonic()
    backup = None
    try:
        selected = copy_bytes(source, stage, engine)
        if stage.stat().st_size != before.st_size:
            raise CopyError(f"Size differs after copy: {source}")
        after = source.stat()
        if (before.st_size, before.st_mtime_ns, before.st_ino) != (
                after.st_size, after.st_mtime_ns, after.st_ino):
            raise CopyError(f"Source changed during copy: {source}")
        source_hash = digest(source, algorithm)
        copied_hash = digest(stage, algorithm)
        if source_hash != copied_hash:
            raise CopyError(f"{algorithm} mismatch: {source}")
        os.chmod(stage, stat.S_IMODE(before.st_mode))
        os.utime(stage, ns=(before.st_atime_ns, before.st_mtime_ns))
        if target.exists():
            if backup_root is None:
                raise CopyError("Backup root required for overwrite")
            backup_root.mkdir(parents=True, exist_ok=True)
            backup = backup_root / (uuid.uuid4().hex + "-" + target.name)
            try:
                os.link(target, backup)
            except OSError:
                shutil.copy2(target, backup)
                with backup.open("rb") as verify_backup:
                    os.fsync(verify_backup.fileno())
            fsync_dir(backup_root)
        # Revalidate the path after hashing to avoid silent symlink overwrite.
        reject_link(target, "destination just before publication")
        if target.exists() and not overwrite:
            raise CopyError(f"Target appeared during copy: {target}")
        os.replace(stage, target)
        fsync_dir(target.parent)
        return {"source": str(source), "destination": str(target),
                "bytes": before.st_size, "engine": selected, "hash": algorithm,
                "verified": True, "backup": str(backup) if backup else None,
                "seconds": round(time.monotonic()-started, 5)}
    finally:
        if stage.exists():
            stage.unlink()


def gather(source: Path) -> list[Path]:
    reject_link(source, "source")
    if source.is_file():
        return [source]
    if not source.is_dir():
        raise CopyError("Source must be a regular file or directory")
    files: list[Path] = []
    for base, dirs, names in os.walk(source, followlinks=False):
        for name in dirs:
            reject_link(Path(base) / name, "directory within source")
        for name in names:
            child = Path(base) / name
            reject_link(child, "file within source")
            if not child.is_file():
                raise CopyError(f"Unsupported special file: {child}")
            files.append(child)
    return sorted(files)


def run_copy(args: argparse.Namespace) -> dict:
    src = Path(args.source).expanduser().absolute()
    dst = Path(args.destination).expanduser().absolute()
    reject_link(src, "source")
    if not src.exists():
        raise CopyError(f"No such source: {src}")
    if args.hash == "blake3" and shutil.which("b3sum") is None:
        raise CopyError("b3sum is required for BLAKE3. Install package b3sum.")
    source_is_dir = src.is_dir()
    if source_is_dir and dst.exists() and dst.is_dir():
        target = dst / src.name
    elif not source_is_dir and dst.exists() and dst.is_dir():
        target = dst / src.name
    else:
        target = dst
    if source_is_dir and (target == src or src in target.parents):
        raise CopyError("Destination must not be inside source directory")
    if target == src:
        raise CopyError("Source and destination are identical")
    inputs = gather(src)
    if source_is_dir:
        outputs = [target / item.relative_to(src) for item in inputs]
    else:
        outputs = [target]
    for item in outputs:
        reject_link(item, "destination")
        if item.exists() and not args.overwrite:
            raise CopyError(f"Destination exists: {item}")
    state = strategy(src, target.parent)
    engine = args.engine
    if engine == "auto":
        engine = "copyfile-range" if state["mode"].startswith("solid") else "buffered"
    workers = args.workers if args.workers else state["workers"]
    if not state["mode"].startswith("solid"):
        workers = 1
    workers = max(1, min(8, workers))
    job_id = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")+"-"+uuid.uuid4().hex[:8]
    backup_root = target.parent / ".solarispkn-recovery" / job_id if args.overwrite else None
    result = {"version": VERSION, "strategy": state["mode"],
              "workers": workers, "hash": args.hash, "files": len(inputs),
              "job_id": job_id, "results": []}
    if args.dry_run:
        result["dry_run"] = True
        result["paths"] = [str(x) for x in outputs[:100]]
        return result

    # No main write occurs before this source/destination plan is validated.
    if source_is_dir and not inputs:
        ensure_parent(target / ".directory-placeholder")
        target.mkdir(parents=True, exist_ok=True)
    actions = list(zip(inputs, outputs))
    try:
        if workers <= 1:
            for source, destination in actions:
                result["results"].append(file_task(source, destination, args.hash,
                                                    engine, args.overwrite, backup_root))
        else:
            with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
                futures = [pool.submit(file_task, source, destination, args.hash,
                                       engine, args.overwrite, backup_root)
                           for source, destination in actions]
                for future in concurrent.futures.as_completed(futures):
                    result["results"].append(future.result())
    except Exception:
        # Intentionally preserve already published, fully verified files and any
        # recovery backups. Never silently roll back destructive operations.
        raise
    result["bytes"] = sum(x["bytes"] for x in result["results"])
    result["verified"] = len(result["results"]) == len(actions)
    return result


def run_benchmark(args: argparse.Namespace) -> dict:
    root = Path(args.root).expanduser().resolve()
    if not root.is_dir():
        raise CopyError("Benchmark root must already exist")
    if args.size_mib < 1 or args.size_mib > 256 or args.rounds < 1 or args.rounds > 5:
        raise CopyError("Benchmark limits: size 1..256 MiB, rounds 1..5")
    size = args.size_mib * MiB
    if shutil.disk_usage(root).free < size * 3 + GiB:
        raise CopyError("Insufficient safety margin (requires 1 GiB + 3x payload)")
    runs = []
    with tempfile.TemporaryDirectory(prefix="solarispkn-paste-owned-",dir=root) as scratch:
        directory = Path(scratch)
        sample = directory / "sample.bin"
        with sample.open("xb") as out:
            block = bytes(range(256)) * 4096
            for _ in range(args.size_mib):
                out.write(block)
            out.flush()
            os.fsync(out.fileno())
        for index in range(args.rounds):
            output = directory / f"result-{index}.bin"
            trial = argparse.Namespace(source=str(sample),destination=str(output),
                hash=args.hash,engine=args.engine,workers=args.workers,
                overwrite=False,dry_run=False)
            before = time.monotonic()
            report = run_copy(trial)
            seconds = time.monotonic()-before
            if not report["verified"]:
                raise CopyError("Benchmark integrity failed")
            runs.append({"round": index+1, "seconds": round(seconds, 5),
                         "MiBps": round(args.size_mib/seconds, 2),
                         "engine": report["results"][0]["engine"],
                         "verified": True})
            output.unlink()
    return {"size_mib": args.size_mib,"algorithm": args.hash,
            "rounds": runs,"method": "copy + independent source/destination hash, fsync included",
            "warning": "Short synthetic file and filesystem cache; not sustained hardware bandwidth"}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="solarispkn-paste",
        description="Linux CLI: verified, staged copy; conservative HDD/RAID routing")
    parser.add_argument("--version",action="version",version=VERSION)
    sub = parser.add_subparsers(dest="command",required=True)
    profile = sub.add_parser("profile",help="Inspect CPU/RAM and a volume (read-only)")
    profile.add_argument("path",nargs="?",default=".")
    copy = sub.add_parser("copy",help="Copy file or directory with atomic staging")
    copy.add_argument("source")
    copy.add_argument("destination")
    copy.add_argument("--hash",choices=["blake3","sha256"],default="blake3")
    copy.add_argument("--engine",choices=["auto","buffered","copyfile-range"],default="auto")
    copy.add_argument("--workers",type=int,choices=range(1,9))
    copy.add_argument("--overwrite",action="store_true")
    copy.add_argument("--dry-run",action="store_true")
    bench = sub.add_parser("benchmark",help="Bounded synthetic benchmark; does not modify real files")
    bench.add_argument("--root",required=True)
    bench.add_argument("--size-mib",type=int,default=16)
    bench.add_argument("--rounds",type=int,default=3)
    bench.add_argument("--hash",choices=["blake3","sha256"],default="blake3")
    bench.add_argument("--engine",choices=["auto","buffered","copyfile-range"],default="auto")
    bench.add_argument("--workers",type=int,default=None)
    args = parser.parse_args(argv)
    try:
        if args.command=="profile":
            result={"version":VERSION,"cpu":cpu_profile(),
                    "volume":media_profile(Path(args.path).expanduser().absolute())}
        elif args.command=="copy":
            result=run_copy(args)
        else:
            result=run_benchmark(args)
        print(json.dumps(result,ensure_ascii=False,indent=2))
        return 0
    except (CopyError,OSError,subprocess.SubprocessError) as exc:
        print(f"SolarisPKN-Paste: {exc}",file=sys.stderr)
        return 2


if __name__=="__main__":
    raise SystemExit(main())
