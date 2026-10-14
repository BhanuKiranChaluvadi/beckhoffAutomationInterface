using BeckhoffAutomationInterface;
using BeckhoffAutomationInterface.Cli;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>Filesystem fixtures for command-line tests: no TwinCAT, just the files the
    /// parser looks at.</summary>
    static class TestProjects
    {
        public static string NewTempDir(string prefix)
        {
            string dir = Path.Combine(Path.GetTempPath(), prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }

        /// <summary>A project as `init` creates it: &lt;root&gt;\&lt;name&gt;\&lt;name&gt;.sln and
        /// &lt;root&gt;\&lt;name&gt;\&lt;name&gt;\&lt;name&gt;.tsproj. Returns the .sln path.</summary>
        public static string Owned(string root, string name)
        {
            string solutionDir = Path.Combine(root, name);
            Directory.CreateDirectory(Path.Combine(solutionDir, name));
            File.WriteAllText(Path.Combine(solutionDir, name, name + ".tsproj"), "<TcSmProject/>");
            string sln = Path.Combine(solutionDir, name + ".sln");
            File.WriteAllText(sln, "");
            return sln;
        }

        /// <summary>A loose project (no .sln), as CI checks out: &lt;root&gt;\&lt;name&gt;\&lt;name&gt;.tsproj.</summary>
        public static string Adopted(string root, string name)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            string tsproj = Path.Combine(dir, name + ".tsproj");
            File.WriteAllText(tsproj, "<TcSmProject/>");
            return tsproj;
        }

        public static string Source(string root, string name)
        {
            string dir = Path.Combine(root, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static RunOptions Parse(string cwd, params string[] args) => CommandLine.Parse(args, cwd: cwd);
    }
}
