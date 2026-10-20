"""Verified boot-file transaction for TwinCAT/BSD, controlled by the build PC.

The caller enters Config over ADS before install/rollback, verifies live identity
after Run, then calls finish. This helper checks the BSD host's Net ID and mode.
It never compiles, changes licenses, or downloads TwinSAFE logic.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

JOURNALS = {'LoggedEvents.db', 'LoggedEvents.db-wal', 'LoggedEvents.db-shm', 'LoggedEvents.db-journal'}
BOOT = Path('/usr/local/etc/TwinCAT/3.1/Boot')


def sha(path):
    result = hashlib.sha256()
    with Path(path).open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            result.update(block)
    return result.hexdigest()


def inventory(root):
    root = Path(root)
    if root.is_symlink():
        raise ValueError('Boot inventory must not follow symlinks')
    result = {}
    for path in sorted(root.rglob('*')):
        if path.is_symlink():
            raise ValueError(f'Symlink in boot files: {path}')
        if path.is_file() and path.name not in JOURNALS:
            result[path.relative_to(root).as_posix()] = sha(path)
    return result


def save(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    temporary.replace(path)


def copy_files(source, destination, files):
    for name, digest in files.items():
        target = destination / name
        if target.exists() and sha(target) == digest:
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / name, target)


def replace_files(source, boot, files):
    before = inventory(boot)
    copy_files(source, boot, files)
    for name in before.keys() - files.keys():
        (boot / name).unlink()
    if inventory(boot) != files:
        raise ValueError('Copied boot files do not match the retained hashes')


def extract_package(package, digest, stage):
    if sha(package) != digest.lower():
        raise ValueError('Package SHA256 differs from the release record')
    with zipfile.ZipFile(package) as archive:
        entries = {}
        for entry in archive.infolist():
            name = entry.filename
            if (name.startswith('/') or '\\' in name or ':' in name or
                    '..' in name.split('/') or '.' in name.split('/') or
                    name in entries or (entry.external_attr >> 16) & 0o170000 == 0o120000):
                raise ValueError('Unsafe or duplicate package entry')
            if not entry.is_dir():
                entries[name] = entry
        manifest = json.loads(archive.read('release.json'))
        if (manifest.get('schemaVersion') != 2 or manifest.get('status') != 'Packaged' or
                manifest.get('kind') != 'ReleaseCandidate' or manifest.get('deploymentApproved') is not False or
                manifest.get('targetPlatform') != 'TwinCAT OS (x64)'):
            raise ValueError('Expected a schema 2 OS x64 release candidate')
        files = {item['path']: item for item in manifest['files']}
        if len(files) != len(manifest['files']) or set(entries) != set(files) | {'release.json'} or 'release.json' in files:
            raise ValueError('Package inventory is incomplete or duplicated')
        required = {'boot/CurrentConfig.xml', 'boot/CurrentProjectInfo.json', 'boot/Plc/Port_851.app',
                    'boot/Plc/Port_851.crc', 'boot/Plc/Port_851.autostart', 'boot/Plc/Port_851_boot.tizip',
                    'boot/Plc/Port_851.tmc', 'identity.expect.json', 'project.zip', 'evidence.json'}
        if not required <= set(files):
            raise ValueError('Package lacks required compiled payload')
        for name, item in files.items():
            with archive.open(name) as stream:
                actual = hashlib.sha256()
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    actual.update(block)
            if actual.hexdigest() != item['sha256'].lower() or entries[name].file_size != item['bytes']:
                raise ValueError(f'Package content differs: {name}')
        for name in files:
            if not name.startswith('boot/'):
                continue
            relative = PurePosixPath(name).relative_to('boot')
            if relative.name in JOURNALS or relative.name.endswith(('.bootdata', '.bootdata-old')):
                raise ValueError('Package must not replace runtime journal or persistent data')
            target = stage.joinpath(*relative.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(name) as source, target.open('xb') as output:
                shutil.copyfileobj(source, output)
    return manifest


def reserve(boot, transaction):
    boot, transaction = Path(boot).resolve(), Path(transaction).resolve()
    if transaction == boot or boot in transaction.parents or transaction in boot.parents:
        raise ValueError('Transaction must be outside the boot directory')
    transaction.mkdir(exist_ok=False)
    lock = boot.parent / 'plc-release.lock'
    with lock.open('x', encoding='utf-8') as stream:
        stream.write(str(transaction))
    return {'state': 'Reserved', 'transaction': str(transaction)}


def install(package, digest, boot, transaction):
    boot, transaction = Path(boot).resolve(), Path(transaction).resolve()
    if not transaction.exists():
        reserve(boot, transaction)
    lock = boot.parent / 'plc-release.lock'
    if lock.read_text(encoding='utf-8') != str(transaction):
        raise ValueError('Transaction does not own the active update lock')
    stage, backup = transaction / 'stage', transaction / 'backup'
    stage.mkdir()
    manifest = extract_package(package, digest, stage)
    current = ET.parse(boot / 'CurrentConfig.xml').getroot()
    incoming = ET.parse(stage / 'CurrentConfig.xml').getroot()
    if current.get('Platform') != manifest['targetPlatform'] or incoming.get('Platform') != manifest['targetPlatform']:
        raise ValueError('Package and runtime boot platforms differ')
    record = transaction / 'transaction.json'
    try:
        before = inventory(boot)
        backup.mkdir()
        copy_files(boot, backup, before)
        if inventory(backup) != before or inventory(boot) != before:
            raise ValueError('Recovery image differs from current boot files')
        persistent = {name: digest for name, digest in before.items() if name.endswith(('.bootdata', '.bootdata-old'))}
        copy_files(backup, stage, persistent)
        data = dict(schemaVersion=1, state='Prepared', boot=str(boot), before=before,
                    installed=inventory(stage), packageSha256=digest, machine=manifest['machine'])
        save(record, data)
        try:
            replace_files(stage, boot, data['installed'])
        except Exception:
            replace_files(backup, boot, before)
            data['state'] = 'RestoredAfterFailure'
            save(record, data)
            raise
        data['state'] = 'Installed'
        save(record, data)
        return data
    except Exception:
        # Keep the lock until the caller restores Run/identity or rolls back.
        raise


def complete(transaction, rollback=False):
    transaction = Path(transaction).resolve()
    record = transaction / 'transaction.json'
    data = json.loads(record.read_text(encoding='utf-8'))
    boot = Path(data['boot'])
    lock = boot.parent / 'plc-release.lock'
    allowed = ('Prepared', 'Installed', 'RestoredAfterFailure') if rollback else ('Installed', 'RolledBack')
    if lock.read_text(encoding='utf-8') != str(transaction) or data['state'] not in allowed:
        raise ValueError('Transaction does not own the active update lock')
    if rollback:
        backup = transaction / 'backup'
        if inventory(backup) != data['before']:
            raise ValueError('Rollback backup changed')
        replace_files(backup, boot, data['before'])
        data['state'] = 'RolledBack'
    else:
        # The running PLC may update persistent data; verify only deployed files.
        actual = inventory(boot)
        expected = data['before'] if data['state'] == 'RolledBack' else data['installed']
        for name, digest in expected.items():
            if not name.endswith(('.bootdata', '.bootdata-old')) and actual.get(name) != digest:
                raise ValueError('Installed boot image changed before final verification')
        data['state'] = 'RollbackVerified' if data['state'] == 'RolledBack' else 'Verified'
    save(record, data)
    if not rollback:
        lock.unlink()
    return data


def assert_host(net_id, mode, run=subprocess.check_output):
    if not re.fullmatch(r'\d{1,3}(\.\d{1,3}){5}', net_id) or any(int(n) > 255 for n in net_id.split('.')):
        raise ValueError('Invalid AMS Net ID')
    identity = run(['TcSysExe.exe', '--netid'], text=True, timeout=15)
    found = re.search(r'AMS Net Id:\s*(\d+(?:\.\d+){5})', identity, re.I)
    if not found or found.group(1) != net_id:
        raise ValueError('SSH host is not the selected ADS target')
    status = run(['TcSysExe.exe', '--mode'], text=True, timeout=15)
    if not re.search(r'\b' + re.escape(mode) + r'\b', status, re.I):
        raise ValueError(f'TwinCAT {mode} mode not confirmed: {status.strip()}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['reserve', 'install', 'rollback', 'finish'])
    parser.add_argument('--transaction', type=Path, required=True)
    parser.add_argument('--net-id', required=True)
    parser.add_argument('--package', type=Path)
    parser.add_argument('--sha256')
    args = parser.parse_args()
    if platform.system() != 'FreeBSD' or os.geteuid() != 0:
        parser.error('Run this helper as root on the selected TwinCAT/BSD host')
    assert_host(args.net_id, 'Run' if args.action in ('reserve', 'finish') else 'Config')
    if args.action == 'reserve':
        result = reserve(BOOT, args.transaction)
    elif args.action == 'install':
        if not args.package or not args.sha256:
            parser.error('install needs --package and --sha256')
        result = install(args.package, args.sha256, BOOT, args.transaction)
    elif args.action == 'rollback' and not (args.transaction / 'transaction.json').exists():
        # Preparation failed before the first boot write. The caller can restore
        # Run and verify the previous identity without a file transaction.
        lock = BOOT.parent / 'plc-release.lock'
        if lock.read_text(encoding='utf-8') != str(args.transaction.resolve()):
            raise ValueError('Transaction does not own the active update lock')
        result = {'schemaVersion': 1, 'state': 'RolledBack', 'boot': str(BOOT), 'before': inventory(BOOT)}
        save(args.transaction / 'transaction.json', result)
    else:
        result = complete(args.transaction, rollback=args.action == 'rollback')
    print(json.dumps({'state': result['state'], 'transaction': str(args.transaction)}))
