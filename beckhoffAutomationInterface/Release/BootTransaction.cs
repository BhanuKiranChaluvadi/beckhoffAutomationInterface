using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BeckhoffAutomationInterface.Machines;
using static BeckhoffAutomationInterface.Release.ArtifactFiles;

namespace BeckhoffAutomationInterface.Release
{
    /// <summary>
    /// The boot-file transaction of a Windows update (file work only). The caller holds the update
    /// lock and has put the runtime in Config. Files are changed in place: TwinCAT keeps its event
    /// journal open, also in Config. Machine persistent data (*.bootdata) stays. The old image is
    /// kept beside the boot folder (Boot.before.&lt;id&gt;), and the journal record allows a rollback.
    /// </summary>
    internal static class BootTransaction
    {
        internal static readonly string[] Journals = { "LoggedEvents.db", "LoggedEvents.db-wal", "LoggedEvents.db-shm", "LoggedEvents.db-journal" };
        static readonly HashSet<string> JournalNames = new HashSet<string>(Journals, StringComparer.OrdinalIgnoreCase);

        internal static List<Dictionary<string, object>> BootInventory(string root) => Evidence(root, "", JournalNames);

        static bool Persistent(string path) => path.EndsWith(".bootdata", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bootdata-old", StringComparison.OrdinalIgnoreCase);

        static void CopyFiles(string source, string destination, IEnumerable<Dictionary<string, object>> files)
        {
            foreach (var file in files)
            {
                string target = Path.Combine(destination, Text(file, "path"));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(source, Text(file, "path")), target, true);
            }
        }

        /// <summary>Makes boot equal to files (taken from source). An unchanged file is not written:
        /// the runtime can hold it open, and a rollback after a locked-file failure needs this.</summary>
        static void SetFiles(string source, string boot, List<Dictionary<string, object>> files)
        {
            var before = Records(BootInventory(boot));
            var desired = Records(files);
            CopyFiles(source, boot, files.Where(f => !before.TryGetValue(Text(f, "path"), out var old) || !Names.Equals(Text(old, "sha256"), Text(f, "sha256"))));
            foreach (var name in before.Keys.Where(n => !desired.ContainsKey(n)))
            {
                string path = Path.GetFullPath(Path.Combine(boot, name));
                if (!Inside(path, boot)) throw new IOException("Obsolete boot file leaves the selected directory.");
                File.Delete(path);
            }
            SameFiles(files, BootInventory(boot), "Installed boot files");
        }

        static void Save(string record, Dictionary<string, object> transaction) =>
            File.WriteAllText(record, Encode(transaction), new UTF8Encoding(false));

        /// <summary>Installs the payload folder into the boot folder. On a copy failure the old image
        /// is put back (state RestoredAfterFailure) and the error is thrown again.</summary>
        internal static Dictionary<string, object> Install(string payload, string boot, string record)
        {
            payload = Path.GetFullPath(payload).TrimEnd('\\');
            boot = Path.GetFullPath(boot).TrimEnd('\\');
            record = Path.GetFullPath(record);
            BuildLocation.Assert(boot, payload); BuildLocation.Assert(payload, boot);
            BuildLocation.Assert(boot, record); BuildLocation.Assert(payload, record);
            if (File.Exists(record)) throw new IOException("Transaction record already exists.");
            foreach (string required in new[] { "CurrentConfig.xml", "Plc/Port_851.app", "Plc/Port_851.crc", "Plc/Port_851.autostart" })
                if (!File.Exists(Path.Combine(payload, required))) throw new InvalidDataException("Missing boot payload: " + required);
            var before = BootInventory(boot);
            if (before.Count == 0) throw new InvalidDataException("Existing boot folder is empty; no recovery image is available.");
            var incoming = Evidence(payload);
            if (incoming.Any(f => JournalNames.Contains(Path.GetFileName(Text(f, "path"))))) throw new InvalidDataException("Payload must not replace the runtime event journal.");
            string parent = Path.GetDirectoryName(boot), id = Guid.NewGuid().ToString("N");
            string stage = Path.Combine(parent, "Boot.stage." + id), backup = Path.Combine(parent, "Boot.before." + id);
            CopyFiles(payload, stage, incoming);
            SameFiles(incoming, Evidence(stage), "Staged boot payload");
            foreach (var file in before.Where(f => Persistent(Text(f, "path"))))
            {
                if (File.Exists(Path.Combine(stage, Text(file, "path")))) throw new InvalidDataException("Payload must not replace machine persistent data.");
                CopyFiles(boot, stage, new[] { file });
            }
            Directory.CreateDirectory(backup);
            CopyFiles(boot, backup, before);
            SameFiles(before, Evidence(backup), "Retained rollback image");
            SameFiles(before, BootInventory(boot), "Current boot before replacement");
            var transaction = new Dictionary<string, object> { ["schemaVersion"] = 2, ["state"] = "Prepared", ["boot"] = boot, ["stage"] = stage,
                ["backup"] = backup, ["before"] = before, ["installed"] = Evidence(stage), ["journalPreserved"] = true };
            Directory.CreateDirectory(Path.GetDirectoryName(record));
            Save(record, transaction);
            try
            {
                SetFiles(stage, boot, (List<Dictionary<string, object>>)transaction["installed"]);
                transaction["state"] = "Installed";
            }
            catch
            {
                transaction["state"] = "Failed";
                SetFiles(backup, boot, before);
                transaction["state"] = "RestoredAfterFailure";
                throw;
            }
            finally { Save(record, transaction); }
            return transaction;
        }

        /// <summary>Puts the retained image back (an incomplete or installed transaction only). The
        /// replaced files are kept in Boot.rolled-back.&lt;id&gt;.</summary>
        internal static Dictionary<string, object> Restore(string record)
        {
            var transaction = Read(record);
            string state = Field(transaction, "state") as string;
            if (Number(transaction, "schemaVersion") != 2 || !new[] { "Prepared", "Failed", "Installed" }.Contains(state))
                throw new InvalidOperationException("Only an incomplete or installed transaction can be rolled back.");
            string boot = Path.GetFullPath(Text(transaction, "boot")).TrimEnd('\\');
            string backup = Path.GetFullPath(Text(transaction, "backup"));
            string parent = Path.GetDirectoryName(boot);
            if (!Names.Equals(Path.GetDirectoryName(backup), parent) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(backup), @"\ABoot\.before\.[0-9a-f]{32}\z"))
                throw new InvalidDataException("Invalid rollback path.");
            var before = Array(transaction, "before").Select(Object).ToList();
            SameFiles(before, Evidence(backup), "Rollback backup");
            string replaced = Path.Combine(parent, "Boot.rolled-back." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(replaced);
            CopyFiles(boot, replaced, BootInventory(boot));
            SetFiles(backup, boot, before);
            transaction["state"] = "RolledBack";
            transaction["replacedImage"] = replaced;
            Save(record, transaction);
            return transaction;
        }
    }
}
