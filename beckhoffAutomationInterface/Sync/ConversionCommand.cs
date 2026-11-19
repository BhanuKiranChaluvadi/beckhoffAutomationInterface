using System;
using System.IO;
using System.Linq;

namespace BeckhoffAutomationInterface.Sync
{
    static class ConversionCommand
    {
        public const string Usage = "bai convert <source-folder> [<source-folder> ...]";

        public static int Run(string[] args)
        {
            try
            {
                if (args.Length < 2 || args.Skip(1).Any(a => a.StartsWith("--", StringComparison.Ordinal)))
                    throw new ArgumentException(Usage);
                var cache = PlcObjectCache.Open();
                foreach (string folder in args.Skip(1))
                {
                    string source = Path.GetFullPath(folder);
                    if (!Directory.Exists(source)) throw new ArgumentException("Source folder not found: " + source);
                    var objects = PlcSourceObject.Group(StFileParser.ParseFolder(source, IgnoreRules.Load(source, new string[0])));
                    foreach (PlcSourceObject item in objects) cache.Get(item);
                    Console.WriteLine("Converted source: {0} ({1} top-level PLC objects).", source, objects.Count);
                }
                Console.WriteLine("Conversion cache: {0} reused, {1} converted ({2}).", cache.Reused, cache.Converted, PlcObjectCache.DefaultDirectory);
                return 0;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Conversion failed: " + ex.Message);
                return 1;
            }
        }
    }
}
