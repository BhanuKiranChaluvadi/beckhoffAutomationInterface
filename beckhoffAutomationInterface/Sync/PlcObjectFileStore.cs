using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>
    /// Object files (.TcPOU/.TcDUT/.TcGVL/.TcIO) that XAE saved after a COM sync, kept so that
    /// another project can import them (CreateChild subtype 58) instead of building each object
    /// over COM again. bai never writes these files itself.
    /// Key: the source texts of the object with its METHODs and PROPERTYs (not its folder), the
    /// TwinCAT version that saved the file, and the bai that sent the texts. A file is stored, and
    /// found again, only while it holds every text of the source (SyncManifest.FileHoldsTexts).
    /// </summary>
    sealed class PlcObjectFileStore
    {
        public const string EnvironmentVariable = "BAI_OBJECT_FILES";

        readonly string _directory;

        public PlcObjectFileStore(string root, string twinCatVersion, string bai)
        {
            if (string.IsNullOrEmpty(twinCatVersion))
                throw new ArgumentException("The TwinCAT version that saved the object files is required.", nameof(twinCatVersion));
            _directory = Path.Combine(Path.GetFullPath(root), PlcSourceObject.Sha256(Encoding.UTF8.GetBytes(twinCatVersion + "\n" + bai)));
        }

        public static string DefaultRoot => Environment.GetEnvironmentVariable(EnvironmentVariable) ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeckhoffAutomationInterface", "object-files");

        /// <summary>Stores the file XAE saved for this object. False (nothing stored) when the
        /// file does not hold every text of the source.</summary>
        public bool Put(IReadOnlyList<StPouSource> group, byte[] file)
        {
            if (!SyncManifest.FileHoldsTexts(SyncManifest.FileText(file), group))
                return false;
            string path = PathOf(group);
            if (Holds(path, group))
                return true;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Each writer owns its temporary file; a reader sees a whole file or none.
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, file);
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (IOException) when (Holds(path, group))
            {
                // Another build stored the same object at the same time.
            }
            finally
            {
                File.Delete(temporary);
            }
            return true;
        }

        /// <summary>The stored file for this object (named as the object), or null.</summary>
        public string Find(IReadOnlyList<StPouSource> group)
        {
            string path = PathOf(group);
            return Holds(path, group) ? path : null;
        }

        /// <summary>Removes the stored file of this object, for example after XAE did not keep its
        /// import: the next build then makes the object over COM.</summary>
        public void Remove(IReadOnlyList<StPouSource> group) => File.Delete(PathOf(group));

        /// <summary>After a sync saved the project: stores each object file that the .plcproj
        /// names and that holds its texts. Returns how many files the store holds for them.</summary>
        public int PutSaved(IEnumerable<StPouSource> sources, string plcprojPath)
        {
            int stored = 0;
            foreach (SyncManifest.SavedFile saved in SyncManifest.SavedFiles(sources, plcprojPath))
                if (Put(saved.Group, saved.Bytes))
                    stored++;
            return stored;
        }

        /// <summary>The TwinCAT version that saved the project (TcVersion of the .tsproj), or null.</summary>
        public static string ProjectTwinCatVersion(string tsprojPath)
        {
            try
            {
                string version = File.Exists(tsprojPath) ? (string)XDocument.Load(tsprojPath).Root.Attribute("TcVersion") : null;
                return string.IsNullOrWhiteSpace(version) ? null : version;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is XmlException)
            {
                return null;
            }
        }

        string PathOf(IReadOnlyList<StPouSource> group) => Path.Combine(_directory,
            PlcSourceObject.SourceHash(group, includeFolder: false), group[0].Name + PlcSourceObject.FileExtension(group[0].Kind));

        static bool Holds(string path, IReadOnlyList<StPouSource> group)
        {
            try
            {
                return File.Exists(path) && SyncManifest.FileHoldsTexts(SyncManifest.FileText(File.ReadAllBytes(path)), group);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
