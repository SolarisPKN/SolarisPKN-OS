#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""Functional-only Merkle regressions; no timing, benchmarks or private assets.

Usage:
    python3 -m unittest discover -s cli/tests -p 'test_verity*.py' -v

Optional host-side dependency: blake3. If absent, BLAKE3 tests are SKIP
and must never be advertised as passing.
"""
import importlib.util
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "tools"))
import verity_image as v


class MerkleRegressionTests(unittest.TestCase):
    SALT = bytes.fromhex("5c" * 32)

    def _paths(self, folder, suffix):
        root = pathlib.Path(folder)
        return root / ("sample-" + suffix + ".img"), root / ("tree-" + suffix + ".bin"), root / ("manifest-" + suffix + ".json")

    def _fixture(self, folder, algorithm):
        image, tree, manifest = self._paths(folder, algorithm)
        # Distinct data blocks exercise more than a constant-zero image.
        with image.open("wb") as f:
            for i in range(130):
                f.write(bytes([(i * 31) & 255]) * 4096)
        built = v.build(image, tree, manifest, algorithm, self.SALT)
        self.assertEqual(built["format"], 1)
        self.assertEqual(built["data_block_size"], 4096)
        self.assertEqual(built["hash_block_size"], 4096)
        self.assertEqual(built["data_blocks"], 130)
        self.assertEqual(built["level_blocks_root_first"], [1, 2])
        self.assertEqual(built["tree_blocks"], 3)
        return image, tree, manifest, built

    def _exercise(self, algorithm):
        with tempfile.TemporaryDirectory() as tmp:
            image, tree, manifest, meta = self._fixture(tmp, algorithm)
            self.assertEqual(
                v.verify_tree(image, tree, algorithm, self.SALT, meta["root_hash"])["status"], "PASS"
            )
            original_image = image.read_bytes()
            original_tree = tree.read_bytes()
            # A changed data byte must be rejected.
            tampered = bytearray(original_image)
            tampered[4096 + 17] ^= 1
            image.write_bytes(tampered)
            with self.assertRaises(ValueError):
                v.verify_tree(image, tree, algorithm, self.SALT, meta["root_hash"])
            image.write_bytes(original_image)
            # A changed non-root tree block must be rejected.
            tampered = bytearray(original_tree)
            tampered[-4096 + 19] ^= 1
            tree.write_bytes(tampered)
            with self.assertRaises(ValueError):
                v.verify_tree(image, tree, algorithm, self.SALT, meta["root_hash"])
            tree.write_bytes(original_tree)
            # A wrong root digest must be rejected.
            wrong_root = ("00" if not meta["root_hash"].startswith("00") else "ff") + meta["root_hash"][2:]
            with self.assertRaises(ValueError):
                v.verify_tree(image, tree, algorithm, self.SALT, wrong_root)
            # A wrong salt must be rejected.
            with self.assertRaises(ValueError):
                v.verify_tree(image, tree, algorithm, bytes.fromhex("b7" * 32), meta["root_hash"])
            # The digest tree must be a full number of hash blocks.
            with tree.open("ab") as output:
                output.write(b"\0")
            with self.assertRaises(ValueError):
                v.verify_tree(image, tree, algorithm, self.SALT, meta["root_hash"])

    def test_sha256_bad_data_tree_root_salt_and_length(self):
        self._exercise("sha256")

    @unittest.skipUnless(importlib.util.find_spec("blake3"), "Optional host blake3 module unavailable: SKIP, not PASS")
    def test_blake3_bad_data_tree_root_salt_and_length(self):
        self._exercise("blake3")

    def test_reject_1024_byte_ext4_geometry(self):
        with tempfile.TemporaryDirectory() as tmp:
            image, tree, manifest = self._paths(tmp, "bad-geometry")
            image.write_bytes(bytes(1024 * 5))
            with self.assertRaises(ValueError):
                v.build(image, tree, manifest, "sha256", self.SALT)

    def test_refuse_existing_outputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            image, tree, manifest = self._paths(tmp, "overwrite")
            image.write_bytes(bytes(4096 * 2))
            tree.write_bytes(b"DO_NOT_OVERWRITE")
            with self.assertRaises(FileExistsError):
                v.build(image, tree, manifest, "sha256", self.SALT)
            self.assertEqual(tree.read_bytes(), b"DO_NOT_OVERWRITE")

    def test_root_first_tree_layout(self):
        with tempfile.TemporaryDirectory() as tmp:
            image, tree, manifest, meta = self._fixture(tmp, "sha256")
            self.assertEqual(len(tree.read_bytes()), 3 * 4096)
            # Root-first order: root block at file offset zero, and it hashes
            # to exactly the advertised root digest.
            computed = v.digest_factory("sha256", self.SALT)(tree.read_bytes()[:4096]).hex()
            self.assertEqual(computed, meta["root_hash"])


if __name__ == "__main__":
    unittest.main()
