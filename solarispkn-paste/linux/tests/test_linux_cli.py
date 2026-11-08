import argparse
import hashlib
from pathlib import Path
import tempfile
import unittest
import sys
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from solarispkn_paste import cli


class CopyTests(unittest.TestCase):
    def setUp(self):
        self.space=tempfile.TemporaryDirectory(prefix="solarispkn-paste-unit-")
        self.root=Path(self.space.name)
        self.src=self.root/"source.bin"
        self.src.write_bytes(bytes(range(256))*8192)

    def tearDown(self):
        self.space.cleanup()

    def params(self,dest,**kwargs):
        opts={"source":str(self.src),"destination":str(dest),
              "hash":"sha256","engine":"buffered","workers":None,
              "overwrite":False,"dry_run":False}
        opts.update(kwargs)
        return argparse.Namespace(**opts)

    def test_verified_file(self):
        dst=self.root/"result.bin"
        report=cli.run_copy(self.params(dst))
        self.assertTrue(report["verified"])
        self.assertEqual(dst.read_bytes(),self.src.read_bytes())
        self.assertEqual(report["bytes"],len(self.src.read_bytes()))

    def test_conflict_never_overwrites_without_opt_in(self):
        dst=self.root/"result.bin"
        dst.write_bytes(b"keep me")
        with self.assertRaises(cli.CopyError):
            cli.run_copy(self.params(dst))
        self.assertEqual(dst.read_bytes(),b"keep me")

    def test_overwrite_preserves_backup(self):
        dst=self.root/"result.bin"
        dst.write_bytes(b"OLD")
        report=cli.run_copy(self.params(dst,overwrite=True))
        self.assertEqual(dst.read_bytes(),self.src.read_bytes())
        self.assertEqual(Path(report["results"][0]["backup"]).read_bytes(),b"OLD")

    def test_reject_symlink_input(self):
        symlink=self.root/"link"
        symlink.symlink_to(self.src)
        with self.assertRaises(cli.CopyError):
            cli.run_copy(self.params(self.root/"other.bin",source=str(symlink)))

    def test_reject_symlink_output(self):
        dst=self.root/"link"
        dst.symlink_to(self.src)
        with self.assertRaises(cli.CopyError):
            cli.run_copy(self.params(dst,overwrite=True))
        self.assertEqual(dst.resolve(),self.src.resolve())

    def test_reject_nested_target(self):
        directory=self.root/"folder"
        directory.mkdir()
        (directory/"f").write_bytes(b"a")
        with self.assertRaises(cli.CopyError):
            cli.run_copy(self.params(directory/"inside",source=str(directory)))

    def test_directory(self):
        directory=self.root/"dir"
        (directory/"nested").mkdir(parents=True)
        (directory/"nested"/"f1").write_bytes(b"hello")
        (directory/"nested"/"f2").write_bytes(b"world")
        dst=self.root/"output"
        report=cli.run_copy(self.params(dst,source=str(directory)))
        self.assertEqual(report["files"],2)
        self.assertEqual((dst/"nested"/"f1").read_bytes(),b"hello")
        self.assertEqual((dst/"nested"/"f2").read_bytes(),b"world")

    def test_dry_run_no_writes(self):
        dst=self.root/"newfile.bin"
        report=cli.run_copy(self.params(dst,dry_run=True))
        self.assertTrue(report["dry_run"])
        self.assertFalse(dst.exists())

    def test_blake3_unavailable_fails_closed(self):
        with patch.object(cli.shutil,"which",return_value=None):
            with self.assertRaises(cli.CopyError):
                cli.run_copy(self.params(self.root/"digest.bin",hash="blake3"))

    def test_sha256_known_answer(self):
        self.assertEqual(cli.digest(self.src,"sha256"),
                         hashlib.sha256(self.src.read_bytes()).hexdigest())

    def test_profile_does_not_write(self):
        before=sorted(self.root.iterdir())
        profile=cli.media_profile(self.root)
        self.assertIn(profile["media"],("hdd","ssd","unknown"))
        self.assertEqual(before,sorted(self.root.iterdir()))

    def test_cpu_limited_route(self):
        with patch.object(cli,"media_profile",side_effect=[
                    {"media":"ssd"},{"media":"ssd"}]), \
             patch.object(cli,"cpu_profile",return_value={
                 "logical_cpus":2,"ram_bytes":8*cli.GiB}):
            self.assertEqual(cli.strategy(self.src,self.root)["workers"],1)

    def test_any_hdd_wins(self):
        with patch.object(cli,"media_profile",side_effect=[
                    {"media":"ssd"},{"media":"hdd"}]):
            route=cli.strategy(self.src,self.root)
        self.assertEqual(route["mode"],"mechanical-queue")
        self.assertEqual(route["workers"],1)

    def test_copyfile_range_or_safe_fallback(self):
        dst=self.root/"kernel.bin"
        report=cli.run_copy(self.params(dst,engine="copyfile-range"))
        self.assertTrue(report["verified"])
        self.assertEqual(dst.read_bytes(),self.src.read_bytes())

    def test_benchmark_owns_and_cleans_temp(self):
        report=cli.run_benchmark(argparse.Namespace(root=str(self.root),
          size_mib=1,rounds=2,hash="sha256",engine="buffered",workers=None))
        self.assertEqual(len(report["rounds"]),2)
        self.assertFalse(any(p.name.startswith("solarispkn-paste-owned-")
                             for p in self.root.iterdir()))

if __name__=="__main__":
    unittest.main()
