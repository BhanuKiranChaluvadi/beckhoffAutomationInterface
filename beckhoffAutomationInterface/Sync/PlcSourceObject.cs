using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>The cache and file unit: one PLC object with all its methods and properties.</summary>
    sealed class PlcSourceObject
    {
        public static StringComparer Names { get; } = StringComparer.OrdinalIgnoreCase;
        static readonly string[] ObjectExtensions = { ".TcPOU", ".TcIO", ".TcDUT", ".TcGVL" };
        public static bool IsObjectFile(string path) => ObjectExtensions.Contains(Path.GetExtension(path), Names);
        public StPouSource Root => Sources[0];
        public IReadOnlyList<StPouSource> Sources { get; }
        public string ContentHash => SourceHash(Sources, includeFolder: false);
        public string FileName => Root.Name + FileExtension(Root.Kind);

        public static string SourceHash(IReadOnlyList<StPouSource> group, bool includeFolder = true)
        {
            var b = new StringBuilder();
            void Add(string s) => b.Append(s == null ? "-1:" : s.Length + ":" + s);
            foreach (StPouSource s in group.Take(1).Concat(group.Skip(1).OrderBy(m => m.Kind).ThenBy(m => m.Name, StringComparer.Ordinal)))
            {
                Add(s.Kind.ToString());
                if (includeFolder) Add(s.RelativeFolder);
                Add(s.Name);
                Add(s.OwnerName);
                Add(s.BaseType);
                Add(s.DeclarationText);
                Add(s.ImplementationText);
                Add(s.GetText);
                Add(s.SetText);
            }
            return Sha256(Encoding.UTF8.GetBytes(b.ToString()));
        }

        public static string FileExtension(PouKind kind)
        {
            switch (kind)
            {
                case PouKind.Interface: return ".TcIO";
                case PouKind.EnumDut:
                case PouKind.StructDut:
                case PouKind.AliasDut: return ".TcDUT";
                case PouKind.Gvl: return ".TcGVL";
                default: return ".TcPOU";
            }
        }

        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        PlcSourceObject(IReadOnlyList<StPouSource> sources) { Sources = sources; }

        public static List<PlcSourceObject> Group(IEnumerable<StPouSource> sources)
        {
            List<StPouSource> all = sources.ToList();
            var names = all.GroupBy(s => s.OwnerName == null ? s.Name : s.OwnerName + "." + s.Name,
                Names);
            string[] duplicates = names.Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
            if (duplicates.Length > 0)
                throw new InvalidOperationException("Duplicate PLC objects: " + string.Join(", ", duplicates));

            var roots = all.Where(s => s.OwnerName == null).ToDictionary(s => s.Name, Names);
            foreach (StPouSource member in all.Where(s => s.OwnerName != null))
            {
                if (!roots.TryGetValue(member.OwnerName, out StPouSource owner) ||
                    !(owner.Kind == PouKind.FunctionBlock || owner.Kind == PouKind.Program || owner.Kind == PouKind.Interface))
                    throw new InvalidOperationException("No method/property owner for " + member.OwnerName + "." + member.Name);
            }
            return roots.Values.OrderBy(s => s.Name, StringComparer.Ordinal)
                .Select(root => new PlcSourceObject(new[] { root }.Concat(all
                    .Where(s => string.Equals(s.OwnerName, root.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => s.Kind).ThenBy(s => s.Name, StringComparer.Ordinal)).ToList())).ToList();
        }
    }
}
