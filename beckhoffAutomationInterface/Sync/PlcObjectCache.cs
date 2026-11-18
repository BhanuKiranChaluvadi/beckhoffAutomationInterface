using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace BeckhoffAutomationInterface.Sync
{
    /// <summary>Immutable, content-addressed conversions shared by independent build projects.</summary>
    sealed class PlcObjectCache
    {
        public const string EnvironmentVariable = "BAI_CONVERSION_CACHE";
        readonly string _directory;
        public int Reused { get; private set; }
        public int Converted { get; private set; }

        public PlcObjectCache(string directory, string converterIdentity)
        {
            string version = PlcSourceObject.Sha256(Encoding.UTF8.GetBytes(PlcObjectXml.Format + "/" + converterIdentity));
            _directory = Path.Combine(Path.GetFullPath(directory), version);
        }

        public static string DefaultDirectory => Environment.GetEnvironmentVariable(EnvironmentVariable) ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BeckhoffAutomationInterface", "conversions");

        public static PlcObjectCache Open() => new PlcObjectCache(DefaultDirectory, SyncManifest.CurrentBai);

        public byte[] Get(PlcSourceObject source)
        {
            string key = source.ContentHash;
            string file = Path.Combine(_directory, key + ".json");
            byte[] bytes = Read(file, key);
            if (bytes != null)
            {
                Reused++;
                return bytes;
            }
            bytes = PlcObjectXml.Bytes(PlcObjectXml.Write(source));
            Converted++;
            Publish(file, key, bytes);
            return bytes;
        }

        public sealed class Entry
        {
            public string Source { get; set; }
            public string Sha256 { get; set; }
            public string Xml { get; set; }
        }

        static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static byte[] Read(string file, string key)
        {
            try
            {
                Entry entry = Serializer().Deserialize<Entry>(File.ReadAllText(file));
                if (entry?.Source != key || entry.Xml == null) return null;
                byte[] bytes = Encoding.UTF8.GetBytes(entry.Xml);
                return PlcSourceObject.Sha256(bytes) == entry.Sha256 ? bytes : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            {
                return null;
            }
        }

        static void Publish(string file, string key, byte[] bytes)
        {
            // Each writer owns a temporary file. Readers see a complete entry or a miss.
            string temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                string json = Serializer().Serialize(new Entry { Source = key, Sha256 = PlcSourceObject.Sha256(bytes), Xml = Encoding.UTF8.GetString(bytes) });
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(temporary, file, null);
                else
                {
                    try { File.Move(temporary, file); }
                    catch (IOException) when (Read(file, key) != null) { /* another writer published the same conversion */ }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine("WARNING: conversion cache not written: " + ex.Message);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }
        }
    }
}
