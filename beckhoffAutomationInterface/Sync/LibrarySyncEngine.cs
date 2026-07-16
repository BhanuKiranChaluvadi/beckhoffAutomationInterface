using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Interop.TCatSysManager;

namespace BeckhoffAutomationInterface.Sync
{
    class LibrarySyncReport
    {
        public List<string> Added { get; } = new List<string>();
        public List<string> Removed { get; } = new List<string>();
    }

    /// <summary>
    /// Reconciles a PLC project's library references (ITcPlcLibraryManager) against
    /// a desired list parsed from a manifest, adding missing references and
    /// removing ones that are no longer desired.
    ///
    /// ITcPlcLibRef exposes a "Name" property assumed (originally, never empirically
    /// confirmed) to be a combined display string like "Tc2_Standard, * (Beckhoff
    /// Automation GmbH)". LIVE VALIDATION (2026-07-16, reverse-export scratch project,
    /// see tasks/2026-07-15-reverse-export-scaffold/) disproved this: the real value is
    /// just the BARE library name, optionally with a leading "#" (e.g. "Tc2_Standard",
    /// "#Tc2_System") — the combined format never actually occurs. TryParseDisplayName
    /// now recognizes both shapes: the (never-observed, kept for safety) combined format,
    /// and the real bare-name format, for which Beckhoff's own system libraries (the
    /// "Tc&lt;N&gt;_*" namespace) default to Version="*"/Company="Beckhoff Automation GmbH" —
    /// the exact convention every libraries.xml in this repo already uses. A bare name
    /// OUTSIDE that namespace is left unparsed (name-only, null version/company) rather
    /// than guessing a company for an unknown/third-party library — callers then leave
    /// such a reference untouched on the write side, or flag it for manual review on the
    /// reverse-export side, rather than risking wrong data either direction.
    /// </summary>
    static class LibrarySyncEngine
    {
        static readonly Regex DisplayNamePattern = new Regex(@"^(?<name>.+?),\s*(?<version>.+?)\s*\((?<company>.+)\)$");

        // The real observed shape: an optional leading "#" (TwinCAT's marker for an
        // implicitly-included reference), then the bare library name.
        static readonly Regex BareNamePattern = new Regex(@"^#?(?<name>.+)$");

        // Beckhoff's own published library namespaces (confirmed throughout this repo's
        // libraries.xml files: Tc2_Standard, Tc2_System, Tc3_Module, Tc3_EventLogger, ...).
        static readonly Regex BeckhoffNamespace = new Regex(@"^Tc\d_");
        const string BeckhoffCompany = "Beckhoff Automation GmbH";

        /// <summary>Parses an ITcPlcLibRef's Name into Name/Version/Company. Tries the
        /// combined display format first (never actually observed live, but harmless to
        /// keep), then the real bare-name format — defaulting Version/Company ONLY for a
        /// recognized Beckhoff "Tc&lt;N&gt;_*" library name. Returns false (name = the raw
        /// value, version/company null) for anything else — callers then leave such a
        /// reference untouched (write side) or flag it for manual review (reverse-export).
        /// isImplicit is true iff the raw display name had a leading "#" — TwinCAT's own
        /// marker (confirmed live, see LIVE FINDING below) for a reference the standard
        /// project template provides automatically, NOT one under explicit user/AddLibrary
        /// control. Callers that consider REMOVING an orphaned reference must skip
        /// isImplicit ones — see the crash this caused, documented on Sync's own orphan
        /// loop below.</summary>
        public static bool TryParseDisplayName(string displayName, out string name, out string version, out string company, out bool isImplicit)
        {
            isImplicit = (displayName ?? "").StartsWith("#", StringComparison.Ordinal);

            Match combined = DisplayNamePattern.Match(displayName ?? "");
            if (combined.Success)
            {
                name = combined.Groups["name"].Value;
                version = combined.Groups["version"].Value;
                company = combined.Groups["company"].Value;
                return true;
            }

            Match bare = BareNamePattern.Match(displayName ?? "");
            if (bare.Success && BeckhoffNamespace.IsMatch(bare.Groups["name"].Value))
            {
                name = bare.Groups["name"].Value;
                version = "*";
                company = BeckhoffCompany;
                return true;
            }

            name = displayName;
            version = null;
            company = null;
            return false;
        }

        /// <summary>Reads every existing library reference as a LibraryReference, for the
        /// reverse (libraries.xml export) direction — see Sync/LibraryManifestWriter.cs.
        /// Enumerates 0-based (ITcPlcReferences is 0-based, unlike ITcSmTreeItem.Child).
        /// Unparseable display names are still returned (Name = full display name, null
        /// Version/Company) and also collected into `unparseable` for the caller to warn.</summary>
        public static List<LibraryReference> ReadReferences(ITcPlcLibraryManager libManager, out List<string> unparseable)
        {
            var result = new List<LibraryReference>();
            unparseable = new List<string>();
            ITcPlcReferences references = libManager.References;
            for (int i = 0; i < references.Count; i++)
            {
                string displayName = references[i].Name;
                if (TryParseDisplayName(displayName, out string name, out string version, out string company, out bool _))
                {
                    result.Add(new LibraryReference(name, version, company));
                }
                else
                {
                    result.Add(new LibraryReference(displayName, null, null));
                    unparseable.Add(displayName);
                }
            }
            return result;
        }

        public static LibrarySyncReport Sync(ITcPlcLibraryManager libManager, IReadOnlyList<LibraryReference> desired)
        {
            var report = new LibrarySyncReport();

            var existing = new List<(string DisplayName, string Name, string Version, string Company, bool IsImplicit)>();
            ITcPlcReferences references = libManager.References;
            // NOTE: unlike ITcSmTreeItem.Child (1-based), ITcPlcReferences is 0-based.
            for (int i = 0; i < references.Count; i++)
            {
                ITcPlcLibRef libRef = references[i];
                string displayName = libRef.Name;
                bool parsed = TryParseDisplayName(displayName, out string name, out string version, out string company, out bool isImplicit);
                existing.Add(parsed
                    ? (displayName, name, version, company, isImplicit)
                    : (displayName, displayName, (string)null, (string)null, isImplicit));
            }

            foreach (LibraryReference lib in desired)
            {
                bool alreadyPresent = existing.Any(e => e.Name == lib.Name && e.Version == lib.Version && e.Company == lib.Company);
                if (alreadyPresent)
                    continue;

                try
                {
                    libManager.AddLibrary(lib.Name, lib.Version, lib.Company);
                    report.Added.Add($"{lib.Name}, {lib.Version} ({lib.Company})");
                }
                catch (ArgumentException ex) when (ex.Message.Contains("already contained"))
                {
                    // The References collection read above can be stale/incomplete right
                    // after a project is (re)opened, so AddLibrary's own duplicate check
                    // is more authoritative than ours — treat this as an idempotent no-op.
                }
            }

            var desiredKeys = new HashSet<string>(desired.Select(d => $"{d.Name}|{d.Version}|{d.Company}"));
            foreach (var e in existing)
            {
                if (e.Version == null) continue; // unparseable display name; don't touch it
                // LIVE FINDING (2026-07-16, reverse-export validation): an implicit ("#"-
                // prefixed) reference is one the standard project template provides
                // automatically, NOT one under explicit AddLibrary/RemoveReference control.
                // Treating it as a normal orphan candidate crashes with COMException
                // "Specified library '...' not found!" the moment it's not in `desired`
                // (true for almost every real manifest, since these implicit references
                // are never meant to be listed explicitly) — confirmed against a real
                // scratch project. Never attempt to remove one.
                if (e.IsImplicit) continue;
                string key = $"{e.Name}|{e.Version}|{e.Company}";
                if (!desiredKeys.Contains(key))
                {
                    libManager.RemoveReference(e.Name, e.Version, e.Company);
                    report.Removed.Add(e.DisplayName);
                }
            }

            return report;
        }
    }
}
