using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static BeckhoffAutomationInterface.Clock;

namespace BeckhoffAutomationInterface.Machines
{
    /// <summary>
    /// 'machine dev': the TwinCAT session stays open; each change in the repository runs one round
    /// (DevPlan decides what it needs). Ends on Ctrl+C, on the stop file, or on a change that needs
    /// a full 'machine test' (a manifest).
    /// </summary>
    static class MachineDev
    {
        /// <summary>Changes are taken together until the repository is quiet this long (an editor
        /// writes a file in several steps; one save of several files is one round).</summary>
        static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1.5);

        /// <param name="run">Runs one round; returns its result. A result that starts with "STOP" ends the loop.</param>
        public static void Watch(string repoRoot, IReadOnlyList<string> sources, string tests, IReadOnlyList<string> manifests,
            string stopFile, Func<DevStep, string> run)
        {
            var changed = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            long lastTicks = 0;
            bool stop = false;
            ConsoleCancelEventHandler cancel = (o, e) => { e.Cancel = true; stop = true; };
            Console.CancelKeyPress += cancel;
            if (File.Exists(stopFile))
                File.Delete(stopFile);
            try
            {
                using (var watcher = new FileSystemWatcher(repoRoot)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                })
                {
                    FileSystemEventHandler seen = (o, e) =>
                    {
                        changed[Relative(repoRoot, e.FullPath)] = 0;
                        Interlocked.Exchange(ref lastTicks, DateTime.UtcNow.Ticks);
                    };
                    watcher.Changed += seen;
                    watcher.Created += seen;
                    watcher.Deleted += seen;
                    watcher.Renamed += (o, e) => { changed[Relative(repoRoot, e.OldFullPath)] = 0; seen(o, e); };
                    watcher.Error += (o, e) => Console.WriteLine("{0}: DEV WARNING — the file watcher lost changes ({1}); save the file again.", Now(), e.GetException().Message);
                    watcher.EnableRaisingEvents = true;

                    int round = 0;
                    Console.WriteLine("{0}: DEV READY — save a .st or scenario file to run a round. Stop: Ctrl+C or create '{1}'.", Now(), stopFile);
                    while (!stop && !File.Exists(stopFile))
                    {
                        Thread.Sleep(500);
                        if (changed.IsEmpty || DateTime.UtcNow.Ticks - Interlocked.Read(ref lastTicks) < Debounce.Ticks)
                            continue;
                        List<string> paths = changed.Keys.ToList();
                        foreach (string p in paths)
                            changed.TryRemove(p, out _);
                        DevStep step = DevPlan.For(paths, sources, tests, manifests);
                        if (step == DevStep.None)
                            continue;
                        if (step == DevStep.Stop)
                        {
                            Console.WriteLine("{0}: DEV STOP — a manifest changed ({1}): run 'bai machine test'.", Now(), string.Join(", ", paths.Take(3)));
                            return;
                        }
                        round++;
                        Console.WriteLine();
                        Console.WriteLine("{0}: DEV RUN {1}: {2} ({3})", Now(), round, step, string.Join(", ", paths.Take(3)) + (paths.Count > 3 ? ", ..." : ""));
                        var clock = Stopwatch.StartNew();
                        string result = run(step);
                        Console.WriteLine("{0}: DEV RESULT {1}: {2} ({3:0} s). Waiting for changes...", Now(), round, result, clock.Elapsed.TotalSeconds);
                        if (result.StartsWith("STOP"))
                            return;
                    }
                    Console.WriteLine("{0}: DEV END.", Now());
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }

        static string Relative(string root, string path) =>
            path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length).TrimStart('\\', '/') : path;
    }
}
