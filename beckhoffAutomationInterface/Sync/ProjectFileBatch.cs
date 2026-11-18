using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>A planned file update with rollback on write failure. Null content means deletion.</summary>
    sealed class ProjectFileBatch
    {
        readonly Dictionary<string, byte[]> _changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _order = new List<string>();

        public void Set(string path, byte[] bytes)
        {
            path = Path.GetFullPath(path);
            if (!_changes.ContainsKey(path)) _order.Add(path);
            _changes[path] = bytes;
        }

        public void Commit(Action<string, byte[]> write = null)
        {
            write = write ?? Write;
            var before = _order.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            var attempted = new List<string>();
            try
            {
                foreach (string path in _order)
                {
                    attempted.Add(path);
                    write(path, _changes[path]);
                }
            }
            catch (Exception failure)
            {
                var rollbackErrors = new List<Exception>();
                foreach (string path in attempted.AsEnumerable().Reverse())
                    try { write(path, before[path]); }
                    catch (Exception ex) { rollbackErrors.Add(ex); }
                if (rollbackErrors.Count > 0)
                    throw new IOException("PLC update failed and rollback was incomplete. Rebuild the project fresh.",
                        new AggregateException(new[] { failure }.Concat(rollbackErrors)));
                throw;
            }
        }

        internal static void Write(string path, byte[] bytes)
        {
            if (bytes == null) { File.Delete(path); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
