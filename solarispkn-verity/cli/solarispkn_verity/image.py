#!/usr/bin/env python3
# SPDX-License-Identifier: MIT
"""SolarisPKN-Verity build-time image/metadata tooling (dm-verity format 1).

This tool doesn't sign roots and doesn't alter dm-verity or the running OS.
BLAKE3 requires the host Python module 'blake3'.  Only use immutable rootfs
artifacts; altering the filesystem after creating a tree invalidates it.
"""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import sys
import tempfile

BLOCK = 4096
DIGEST = 32
PER_BLOCK = BLOCK // DIGEST


def digest_factory(algorithm, salt):
    if algorithm == 'sha256':
        def digest(data):
            h = hashlib.sha256()
            h.update(salt)
            h.update(data)
            return h.digest()
        return digest
    if algorithm == 'blake3':
        try:
            import blake3
        except ImportError as ex:
            raise RuntimeError("BLAKE3 requiere el modulo 'blake3' de Python SOLO en el host constructor") from ex
        def digest(data):
            h = blake3.blake3()
            h.update(salt)
            h.update(data)
            return h.digest(length=DIGEST)
        return digest
    raise ValueError('Algoritmo no reconocido')


def input_blocks(path):
    size = path.stat().st_size
    if size < BLOCK or size % BLOCK:
        raise ValueError('Imagen no alineada a 4096; cree una copia alineada fuera de este programa')
    return size // BLOCK


def make_leaf_level(image, dst, digest):
    count = 0
    with image.open('rb') as inp, dst.open('wb') as out:
        while True:
            chunk = inp.read(BLOCK)
            if not chunk:
                break
            if len(chunk) != BLOCK:
                raise ValueError('Imagen truncada durante la lectura')
            out.write(digest(chunk))
            count += 1
            if count % PER_BLOCK == 0:
                continue
            # A partial hash-block is only padded at the end.
        missing = (-count) % PER_BLOCK
        out.write(b'\x00' * (missing * DIGEST))
    return (count + PER_BLOCK - 1) // PER_BLOCK


def make_parent_level(level_file, dst, digest):
    count = 0
    with level_file.open('rb') as inp, dst.open('wb') as out:
        while True:
            block = inp.read(BLOCK)
            if not block:
                break
            if len(block) != BLOCK:
                raise ValueError('Arbol truncado durante creacion')
            out.write(digest(block))
            count += 1
        out.write(b'\x00' * ((-count % PER_BLOCK) * DIGEST))
    return (count + PER_BLOCK - 1) // PER_BLOCK


def sha256_file(p):
    h = hashlib.sha256()
    with p.open('rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''):
            h.update(chunk)
    return h.hexdigest()


def build(image, tree, manifest, algo, salt, overwrite=False):
    blocks = input_blocks(image)
    if len({image.resolve(), tree.resolve(), manifest.resolve()}) != 3:
        raise ValueError('Las rutas de imagen, arbol y manifiesto deben ser diferentes')
    digest = digest_factory(algo, salt)
    if not overwrite and (tree.exists() or manifest.exists()):
        raise FileExistsError('Salida existente: no se sobrescribe sin --overwrite')
    tree.parent.mkdir(parents=True, exist_ok=True)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='solarispkn-verity-') as td:
        work = pathlib.Path(td)
        files = [work / 'level-0']
        sizes = [make_leaf_level(image, files[0], digest)]
        while sizes[-1] > 1:
            parent = work / ('level-' + str(len(files)))
            sizes.append(make_parent_level(files[-1], parent, digest))
            files.append(parent)
        with files[-1].open('rb') as f:
            root_block = f.read(BLOCK)
        root = digest(root_block).hex()
        tmp_tree = work / 'tree.bin'
        with tmp_tree.open('wb') as dst:
            for f in reversed(files):
                with f.open('rb') as src:
                    shutil.copyfileobj(src, dst, 1024 * 1024)
        result = {
            'schema': 1, 'format': 1, 'algorithm': algo,
            'data_block_size': BLOCK, 'hash_block_size': BLOCK,
            'data_blocks': blocks, 'hash_start_block': 0,
            'tree_blocks': sum(sizes), 'level_blocks_root_first': list(reversed(sizes)),
            'root_hash': root, 'salt_hex': salt.hex(),
            'data_file': str(image.resolve()), 'tree_file': str(tree.resolve()),
            'data_sha256': sha256_file(image), 'tree_sha256': sha256_file(tmp_tree),
            'sector_count': blocks * (BLOCK // 512),
        }
        # Validate the full tree before installing outputs.
        verify_tree(image, tmp_tree, algo, salt, root, sizes)
        if overwrite:
            tree.unlink(missing_ok=True)
            manifest.unlink(missing_ok=True)
        shutil.move(str(tmp_tree), tree)
        manifest.write_text(json.dumps(result, indent=2, sort_keys=True) + '\n')
        return result


def verify_tree(image, tree, algo, salt, root_hex, expected_sizes=None):
    """Check *all* data digests and all levels against the supplied tree."""
    blocks = input_blocks(image)
    digest = digest_factory(algo, salt)
    sizes = []
    n = (blocks + PER_BLOCK - 1) // PER_BLOCK
    while True:
        sizes.append(n)
        if n == 1:
            break
        n = (n + PER_BLOCK - 1) // PER_BLOCK
    if expected_sizes is not None and sizes != expected_sizes:
        raise ValueError('Conteo de niveles inconsistente')
    if tree.stat().st_size != sum(sizes) * BLOCK:
        raise ValueError('Tamanio de arbol inconsistente')
    offsets = []
    offset = 0
    for size in reversed(sizes):
        offsets.append(offset)
        offset += size * BLOCK
    offsets = list(reversed(offsets))   # offsets[0] = leaves
    with tempfile.TemporaryDirectory(prefix='solarispkn-verify-') as td:
        work = pathlib.Path(td)
        level = work / 'leaf'
        make_leaf_level(image, level, digest)
        with tree.open('rb') as t:
            for i, num_blocks in enumerate(sizes):
                t.seek(offsets[i])
                with level.open('rb') as f:
                    for j in range(num_blocks):
                        expected = f.read(BLOCK)
                        observed = t.read(BLOCK)
                        if expected != observed:
                            raise ValueError(f'Tree mismatch: level {i}, hash block {j}')
                if num_blocks > 1:
                    nxt = work / f'level-{i+1}'
                    make_parent_level(level, nxt, digest)
                    level = nxt
        with level.open('rb') as f:
            calculated_root = digest(f.read(BLOCK)).hex()
        if calculated_root != root_hex.lower():
            raise ValueError('Root hash no coincide con el arbol')
    return {'status': 'PASS', 'algorithm': algo, 'data_blocks': blocks,
            'tree_blocks': sum(sizes), 'root_hash': root_hex.lower()}


def load_manifest(p):
    m = json.loads(p.read_text())
    if m.get('format') != 1 or m.get('data_block_size') != BLOCK or m.get('hash_block_size') != BLOCK:
        raise ValueError('Manifiesto no compatible con dm-verity v1 y bloques 4096')
    return m


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest='action', required=True)
    a = sub.add_parser('build', help='crea árbol y manifiesto para una imagen inmutable')
    a.add_argument('--image', type=pathlib.Path, required=True)
    a.add_argument('--tree', type=pathlib.Path, required=True)
    a.add_argument('--manifest', type=pathlib.Path, required=True)
    a.add_argument('--algorithm', choices=['sha256', 'blake3'], required=True)
    a.add_argument('--salt', help='sal en hexadecimal (por defecto, aleatoria de 32 bytes)')
    a.add_argument('--overwrite', action='store_true')
    b = sub.add_parser('verify', help='valida bytes de todos los niveles y root hash')
    b.add_argument('--manifest', type=pathlib.Path, required=True)
    b.add_argument('--image', type=pathlib.Path, required=True)
    b.add_argument('--tree', type=pathlib.Path, required=True)
    ext = sub.add_parser('audit', help='revisa un arbol existente sin modificarlo')
    ext.add_argument('--image', type=pathlib.Path, required=True)
    ext.add_argument('--tree', type=pathlib.Path, required=True)
    ext.add_argument('--algorithm', choices=['sha256', 'blake3'], required=True)
    ext.add_argument('--salt', required=True)
    ext.add_argument('--root', required=True)
    c = sub.add_parser('table', help='imprime SOLO la tabla dm-verity (sin crear dispositivos)')
    c.add_argument('--manifest', type=pathlib.Path, required=True)
    c.add_argument('--data-device', required=True)
    c.add_argument('--hash-device', required=True)
    args = p.parse_args(argv)
    try:
        if args.action == 'build':
            salt = bytes.fromhex(args.salt) if args.salt is not None else os.urandom(32)
            if len(salt) > 256:
                raise ValueError('Sal demasiado larga')
            result = build(args.image, args.tree, args.manifest, args.algorithm, salt, args.overwrite)
        elif args.action == 'audit':
            salt = bytes.fromhex(args.salt)
            d = digest_factory(args.algorithm, salt)
            with args.tree.open('rb') as f:
                observed_root = d(f.read(BLOCK)).hex()
            print('ROOT_CALCULADO_DEL_PRIMER_BLOQUE:', observed_root, flush=True)
            print('ROOT_ESPERADO:', args.root.lower(), flush=True)
            result = verify_tree(args.image, args.tree, args.algorithm, salt, args.root)
        elif args.action == 'verify':
            m = load_manifest(args.manifest)
            if sha256_file(args.image) != m['data_sha256']:
                raise ValueError('La imagen no coincide con el manifiesto SHA256')
            if sha256_file(args.tree) != m['tree_sha256']:
                raise ValueError('El arbol no coincide con el manifiesto SHA256')
            if input_blocks(args.image) != m['data_blocks']:
                raise ValueError('El numero de bloques cambió')
            result = verify_tree(args.image, args.tree, m['algorithm'], bytes.fromhex(m['salt_hex']), m['root_hash'])
        else:
            m = load_manifest(args.manifest)
            print(f"0 {m['sector_count']} verity 1 {args.data_device} {args.hash_device} "
                  f"{m['data_block_size']} {m['hash_block_size']} {m['data_blocks']} "
                  f"{m['hash_start_block']} {m['algorithm']} {m['root_hash']} "
                  f"{m['salt_hex'] or '-'}")
            return 0
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except (ValueError, OSError, RuntimeError) as e:
        print('ERROR:', e, file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
