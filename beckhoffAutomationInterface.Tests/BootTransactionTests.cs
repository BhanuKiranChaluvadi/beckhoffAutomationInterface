using System;
using System.IO;
using BeckhoffAutomationInterface.Release;
using Xunit;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>The Windows boot-file transaction (was Tests/Tools/test_boot_transaction.ps1): install,
    /// persistent data, backup, rollback and the negative cases. File system only, no runtime.</summary>
    public sealed class BootTransactionTests : IDisposable
    {
        readonly string work = TestProjects.NewTempDir("bai_boot");
        string Boot => Path.Combine(work, "Boot");
        string Payload => Path.Combine(work, "payload");
        public void Dispose() { try { Directory.Delete(work, true); } catch (IOException) { } }

        public BootTransactionTests()
        {
            foreach (string folder in new[] { "Boot", "payload" })
                foreach (string file in new[] { "CurrentConfig.xml", "Plc/Port_851.app", "Plc/Port_851.crc", "Plc/Port_851.autostart" })
                    Write(folder + "/" + file, folder + "/" + file);
            Write("Boot/Plc/Port_851.bootdata", "machine persistent values");
            Write("Boot/LoggedEvents.db", "runtime journal");
        }

        void Write(string relative, string text)
        {
            string path = Path.Combine(work, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
        }

        string Text(string relative) => File.ReadAllText(Path.Combine(work, relative));

        [Fact]
        public void InstallKeepsPersistentDataAndJournalAndRollsBack()
        {
            var before = BootTransaction.BootInventory(Boot);
            using (new FileStream(Path.Combine(Boot, "LoggedEvents.db"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var transaction = BootTransaction.Install(Payload, Boot, Path.Combine(work, "transaction.json"));
                Assert.Equal("Installed", transaction["state"]);
                Assert.Equal("payload/Plc/Port_851.app", Text("Boot/Plc/Port_851.app"));
                Assert.Equal("machine persistent values", Text("Boot/Plc/Port_851.bootdata"));
                SameFiles(before, Evidence((string)transaction["backup"]), "Retained backup");

                var restored = BootTransaction.Restore(Path.Combine(work, "transaction.json"));
                Assert.Equal("RolledBack", restored["state"]);
                SameFiles(before, BootTransaction.BootInventory(Boot), "Complete rollback");
                Assert.Throws<InvalidOperationException>(() => BootTransaction.Restore(Path.Combine(work, "transaction.json")));
            }
            Assert.Equal("runtime journal", Text("Boot/LoggedEvents.db"));
        }

        [Fact]
        public void CorruptBackupIsRefusedAndActiveFilesStay()
        {
            var transaction = BootTransaction.Install(Payload, Boot, Path.Combine(work, "second.json"));
            File.WriteAllText(Path.Combine((string)transaction["backup"], "Plc", "Port_851.app"), "corrupt backup");
            Assert.Throws<InvalidDataException>(() => BootTransaction.Restore(Path.Combine(work, "second.json")));
            Assert.Equal("payload/Plc/Port_851.app", Text("Boot/Plc/Port_851.app"));
        }

        [Fact]
        public void LockedDestinationRestoresThePreviousImage()
        {
            // Reads stay allowed; the write of a later payload file fails after earlier files changed.
            var before = BootTransaction.BootInventory(Boot);
            using (new FileStream(Path.Combine(Boot, "Plc", "Port_851.crc"), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => BootTransaction.Install(Payload, Boot, Path.Combine(work, "locked.json")));
            SameFiles(before, BootTransaction.BootInventory(Boot), "Partial copy rollback");
            Assert.Equal("RestoredAfterFailure", Read(Path.Combine(work, "locked.json"))["state"]);
        }

        [Fact]
        public void PayloadMustNotReplaceJournalOrPersistentData()
        {
            Write("payload/Plc/Port_851.bootdata", "other values");
            Assert.Contains("persistent data", Assert.Throws<InvalidDataException>(() => BootTransaction.Install(Payload, Boot, Path.Combine(work, "t1.json"))).Message);
            File.Delete(Path.Combine(Payload, "Plc", "Port_851.bootdata"));
            Write("payload/LoggedEvents.db", "journal");
            Assert.Contains("journal", Assert.Throws<InvalidDataException>(() => BootTransaction.Install(Payload, Boot, Path.Combine(work, "t2.json"))).Message);
        }

        [Fact]
        public void IncompletePayloadOrEmptyBootIsRefused()
        {
            File.Delete(Path.Combine(Payload, "Plc", "Port_851.crc"));
            Assert.Throws<InvalidDataException>(() => BootTransaction.Install(Payload, Boot, Path.Combine(work, "t.json")));
            Write("payload/Plc/Port_851.crc", "x");
            string empty = Path.Combine(work, "Empty", "Boot");
            Directory.CreateDirectory(empty);
            Assert.Contains("no recovery image", Assert.Throws<InvalidDataException>(() => BootTransaction.Install(Payload, empty, Path.Combine(work, "t.json"))).Message);
        }
    }
}
