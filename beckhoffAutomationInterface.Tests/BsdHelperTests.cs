using System;
using System.IO;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The TwinCAT/BSD boot transaction helper (embedded bsd_boot_transaction.py; was
    /// Tests/Tools/test_bsd_boot_transaction.py). Each case runs the real helper in python on
    /// local folders, with the setup below. Not a BSD kernel or ADS runtime test.</summary>
    public sealed class BsdHelperTests : IDisposable
    {
        readonly string work = TestProjects.NewTempDir("bai_bsd");
        public void Dispose() { try { Directory.Delete(work, true); } catch (IOException) { } }

        const string Setup = @"
import hashlib, json, re, sys, zipfile, importlib.util
from pathlib import Path
from unittest.mock import patch
spec = importlib.util.spec_from_file_location('bsd_boot', sys.argv[1])
boot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(boot)
root = Path(sys.argv[2])
active = root / 'Boot'
(active / 'Plc').mkdir(parents=True)
(active / 'CurrentConfig.xml').write_text('<TcBootProject Platform=""TwinCAT OS (x64)""/>')
(active / 'Plc/Port_851.app').write_text('old application')
(active / 'Plc/Port_851.bootdata').write_text('machine values')
(active / 'LoggedEvents.db').write_text('journal')
before = boot.inventory(active)
transaction = root / 'transaction'
package = root / 'candidate.zip'
payload = {'boot/CurrentConfig.xml': b'<TcBootProject Platform=""TwinCAT OS (x64)""/>',
           'boot/CurrentProjectInfo.json': b'{}', 'identity.expect.json': b'{}',
           'project.zip': b'fixture', 'evidence.json': b'{}'}
for name in ('Port_851.app', 'Port_851.crc', 'Port_851.autostart', 'Port_851_boot.tizip', 'Port_851.tmc'):
    payload['boot/Plc/' + name] = b'new fixture ' + name.encode()
def write_package():
    manifest = dict(schemaVersion=2, status='Packaged', kind='ReleaseCandidate', deploymentApproved=False,
                    targetPlatform='TwinCAT OS (x64)', machine='NFPro',
                    files=[dict(path=name, bytes=len(value), sha256=hashlib.sha256(value).hexdigest()) for name, value in payload.items()])
    with zipfile.ZipFile(package, 'w') as archive:
        for name, value in payload.items():
            archive.writestr(name, value)
        archive.writestr('release.json', json.dumps(manifest))
write_package()
def install():
    return boot.install(package, boot.sha(package), active, transaction)
def raises(kind, pattern, action):
    try:
        action()
    except kind as error:
        assert re.search(pattern, str(error)), str(error)
        return
    raise AssertionError('expected ' + kind.__name__)
";

        void Check(string body)
        {
            string script = Path.Combine(work, "case.py");
            File.WriteAllText(script, (Setup + body).Replace("\r\n", "\n") + "\nprint('PASS')\n");
            string root = Path.Combine(work, "root");
            Directory.CreateDirectory(root);
            var (code, output) = Python.Run(script, null, Python.Helper(work), root);
            Assert.True(code == 0 && output.Contains("PASS"), output);
        }

        [PythonFact]
        public void InstallAndRollbackKeepMachineData() => Check(@"
assert install()['state'] == 'Installed'
assert (active / 'Plc/Port_851.bootdata').read_text() == 'machine values'
assert (active / 'LoggedEvents.db').read_text() == 'journal'
assert boot.complete(transaction, rollback=True)['state'] == 'RolledBack'
assert boot.inventory(active) == before
assert (root / 'plc-release.lock').exists()
assert boot.complete(transaction)['state'] == 'RollbackVerified'
assert not (root / 'plc-release.lock').exists()
");

        [PythonFact]
        public void PartialCopyFailureRestoresPreviousImage() => Check(@"
original = boot.shutil.copy2
failed = []
def fail_once(source, target):
    if Path(target).resolve() == (active / 'Plc/Port_851.crc').resolve() and not failed:
        failed.append(True)
        raise OSError('injected disk write failure')
    return original(source, target)
with patch.object(boot.shutil, 'copy2', side_effect=fail_once):
    raises(OSError, 'disk write', install)
assert failed
assert boot.inventory(active) == before
assert json.loads((transaction / 'transaction.json').read_text())['state'] == 'RestoredAfterFailure'
");

        [PythonFact]
        public void CorruptBackupCannotReplaceCurrentImage() => Check(@"
install()
(transaction / 'backup/Plc/Port_851.app').write_text('corrupt')
current = boot.inventory(active)
raises(ValueError, 'backup changed', lambda: boot.complete(transaction, rollback=True))
assert boot.inventory(active) == current
assert (root / 'plc-release.lock').exists()
");

        [PythonFact]
        public void FinishAllowsRuntimePersistentUpdates() => Check(@"
install()
(active / 'Plc/Port_851.bootdata').write_text('new runtime value')
assert boot.complete(transaction)['state'] == 'Verified'
");

        [PythonFact]
        public void SecondUpdaterCannotTakeTheLock() => Check(@"
boot.reserve(active, transaction)
raises(FileExistsError, '', lambda: boot.reserve(active, root / 'second'))
assert (root / 'plc-release.lock').read_text() == str(transaction.resolve())
assert boot.inventory(active) == before
");

        [PythonFact]
        public void RejectsHashPlatformAndUnsafePaths() => Check(@"
raises(ValueError, 'SHA256', lambda: boot.extract_package(package, '0' * 64, root / 'stage'))
payload['boot/../../outside'] = b'bad'
write_package()
raises(ValueError, 'Unsafe', lambda: boot.extract_package(package, boot.sha(package), root / 'stage'))
assert boot.inventory(active) == before
");

        [PythonFact]
        public void RemoteHostAndModeMustMatchAdsTarget() => Check(@"
def run(args, **kwargs):
    return 'AMS Net Id: 1.2.3.4.1.1' if args[-1] == '--netid' else 'TwinCAT Config'
boot.assert_host('1.2.3.4.1.1', 'Config', run)
raises(ValueError, 'selected ADS target', lambda: boot.assert_host('1.2.3.5.1.1', 'Config', run))
raises(ValueError, 'Run mode not confirmed', lambda: boot.assert_host('1.2.3.4.1.1', 'Run', run))
");
    }
}
