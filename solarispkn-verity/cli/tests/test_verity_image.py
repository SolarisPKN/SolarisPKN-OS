#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
import importlib.util
import json
import pathlib
import tempfile
import unittest
import sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]/'tools'))
import verity_image as v

class VerityImageTests(unittest.TestCase):
    def run_case(self, algo):
        with tempfile.TemporaryDirectory() as td:
            d=pathlib.Path(td)
            image=d/'rootfs.ext4'
            image.write_bytes(b'\0'*4096*130)
            tree=d/'rootfs.tree'
            manifest=d/'rootfs.json'
            m=v.build(image,tree,manifest,algo,bytes.fromhex('a5'*32))
            self.assertEqual(m['data_blocks'],130)
            self.assertEqual(m['level_blocks_root_first'],[1,2])
            self.assertEqual(m['tree_blocks'],3)
            self.assertEqual(v.verify_tree(image,tree,algo,bytes.fromhex('a5'*32),m['root_hash'])['status'],'PASS')
            image.write_bytes(b'X'+image.read_bytes()[1:])
            with self.assertRaises(ValueError):
                v.verify_tree(image,tree,algo,bytes.fromhex('a5'*32),m['root_hash'])
            image.write_bytes(b'\0'*4096*130)
            bad=bytearray(tree.read_bytes());bad[0]^=1
            tree.write_bytes(bad)
            with self.assertRaises(ValueError):
                v.verify_tree(image,tree,algo,bytes.fromhex('a5'*32),m['root_hash'])
    def test_sha256(self):
        self.run_case('sha256')
    @unittest.skipUnless(importlib.util.find_spec('blake3'),'blake3 no instalado en este ambiente')
    def test_blake3(self):
        self.run_case('blake3')
    def test_two_block_sha256_known_vector(self):
        import hashlib
        with tempfile.TemporaryDirectory() as td:
            d=pathlib.Path(td); img=d/'z.img';img.write_bytes(bytes(8192))
            m=v.build(img,d/'t',d/'m','sha256',b'')
            self.assertEqual(m['root_hash'],'90b10db56173e2e7c847f6ff3ed85a3aebb140bc0064ec9a156ae5b2e71db988')
            self.assertEqual(m['level_blocks_root_first'],[1])
if __name__=='__main__':unittest.main()
