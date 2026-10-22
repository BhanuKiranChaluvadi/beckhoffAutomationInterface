using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BeckhoffAutomationInterface.Cli;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class CliPresentationTests
    {
        public static IEnumerable<object[]> HelpTopics()
        {
            yield return new object[] { "" };
            foreach (var command in CliHelp.Commands)
            {
                yield return new object[] { command.Name };
                foreach (string mode in command.Modes) yield return new object[] { command.Name + " " + mode };
            }
        }

        [Theory]
        [MemberData(nameof(HelpTopics))]
        public void HelpCoversEveryPublicCommandAndModeWithoutSideEffects(string topic)
        {
            var result = Run("help " + topic);
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains("Usage:", result.Output);
            Assert.Contains("Examples:", result.Output);
            Assert.Contains("--help", result.Output);
            Assert.DoesNotContain("\u001b", result.Output);
            Assert.DoesNotContain("See the command reference", result.Output); // every option has a description
            if (topic.Length > 0) Assert.Contains("bai " + topic, result.Output);
        }

        [Theory]
        [InlineData("package verify", "package create", "--directory")]
        [InlineData("libraries verify", "toolchain capture", "--archive")]
        [InlineData("release lock", "release build", "--bundle")]
        [InlineData("export object", "export code", "--all-variants")]
        [InlineData("machine test", "machine build", "--parallel")]
        public void NestedHelpOnlyShowsRelevantSyntaxAndOptions(string topic, string unrelated, string unrelatedOption)
        {
            var result = Run(topic + " --help");
            Assert.Equal(0, result.Exit);
            Assert.DoesNotContain(unrelated, result.Output);
            Assert.DoesNotContain(unrelatedOption, result.Output);
            Assert.Equal(Run("help " + topic).Output, result.Output);
        }

        [Theory]
        [InlineData("--help", 0, "Commands:")]
        [InlineData("-h", 0, "Commands:")]
        [InlineData("", 1, "Commands:")]
        [InlineData("--version", 0, "bai ")]
        [InlineData("--shell xae", 1, "Usage:")]
        [InlineData("--shell xae build --help", 0, "bai build")]
        public void GlobalRequestsHaveExpectedExitCodes(string arguments, int exit, string expected)
        {
            var result = Run(arguments);
            Assert.Equal(exit, result.Exit);
            Assert.Contains(expected, result.Output);
            Assert.Empty(result.Error);
        }

        [Theory]
        [InlineData("biuld", 1, "Did you mean 'bai build'?")]
        [InlineData("build --shell", 1, "--shell needs a value")]
        [InlineData("build --shell --clean", 1, "--shell needs a value")]
        [InlineData("build --shell xae --shell vs2022", 1, "may only be specified once")]
        [InlineData("build --plc --clean", 1, "--plc needs a value")]
        [InlineData("verify --target --expect file.json", 1, "--target needs a value")]
        [InlineData("machine build --work --fresh", 2, "--work needs a value")]
        [InlineData("doctor --invalid", 1, "does not accept arguments")]
        [InlineData("package verify --invalid", 2, "Unknown or duplicate option")]
        [InlineData("help missing", 1, "Unknown help topic")]
        [InlineData("package missing --help", 2, "Unknown help topic")]
        public void InvalidArgumentsUseStderrAndKeepTheirExitCode(string arguments, int exit, string expected)
        {
            var result = Run(arguments);
            Assert.Equal(exit, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains("ERROR:", result.Error);
            Assert.Contains(expected, result.Error);
            Assert.Contains("--help", result.Error);
            Assert.DoesNotContain("Unhandled Exception", result.Error);
        }

        [Theory]
        [InlineData(40)]
        [InlineData(60)]
        [InlineData(80)]
        public void HelpWrapsDescriptionsAtTheRequestedWidth(int width)
        {
            using (var output = new StringWriter())
            using (var error = new StringWriter())
            {
                Assert.Equal(0, CliHelp.Write(output, error, new[] { "release", "build" }, width));
                Assert.All(output.ToString().Split('\n'), line => Assert.True(line.TrimEnd('\r').Length <= width, line));
                Assert.Empty(error.ToString());
            }
        }

        [Fact]
        public void FieldsHandleGenericDictionariesNestedListsAndCopyablePaths()
        {
            using (var output = new StringWriter())
            {
                new ConsoleLayout(output, 40).Fields(new Dictionary<string, object>
                {
                    ["file"] = @"C:\a long folder\release.zip",
                    ["passed"] = true,
                    ["missing"] = null,
                    ["files"] = new[] { new Dictionary<string, object> { ["name"] = "boot.app", ["size"] = 42 } },
                    ["empty"] = new string[0],
                });
                string report = output.ToString();
                Assert.Contains(@"C:\a long folder\release.zip", report);
                Assert.Contains("yes", report);
                Assert.Contains("(none)", report);
                Assert.Contains("boot.app", report);
                Assert.Contains("42", report);
                Assert.Contains("empty: (none)", report);
                Assert.DoesNotContain("System.Collections", report);
            }
        }

        [Fact]
        public void OfflineReportsAreReadableAndJsonRemainsParseable()
        {
            string root = TestProjects.NewTempDir("bai_cli");
            try
            {
                foreach (string directory in new[] { "system", "devices", "user" })
                    Directory.CreateDirectory(Path.Combine(root, directory));
                File.WriteAllText(Path.Combine(root, "system", "device.xml"), "<EtherCATInfo/>");
                File.Copy(Path.Combine(root, "system", "device.xml"), Path.Combine(root, "user", "device.xml"));
                string args = "esi verify --system-directory \"" + Path.Combine(root, "system")
                    + "\" --devices \"" + Path.Combine(root, "devices") + "\" --user-directory \"" + Path.Combine(root, "user") + "\"";
                var text = Run(args);
                Assert.Equal(0, text.Exit);
                Assert.Empty(text.Error);
                Assert.StartsWith("PASS: esi verify", text.Output);
                Assert.DoesNotContain("{\"", text.Output);

                var json = Run(args + " --json");
                Assert.Equal(0, json.Exit);
                Assert.Empty(json.Error);
                Assert.Equal("Passed", Release.ArtifactFiles.Json(json.Output)["status"]);
                Assert.DoesNotContain("PASS:", json.Output);

                string source = Path.Combine(root, "source");
                Directory.CreateDirectory(source);
                File.WriteAllText(Path.Combine(source, "MAIN.st"), "PROGRAM MAIN\nVAR\nEND_VAR\nEND_PROGRAM\n");
                var parse = Run("check parse \"" + source + "\" --no-config");
                Assert.Equal(0, parse.Exit);
                Assert.Empty(parse.Error);
                Assert.Contains("bai check parse", parse.Output);
                Assert.Contains(source, parse.Output);
                Assert.Contains("1 file(s) parsed OK", parse.Output);
                Assert.DoesNotContain("Dest=", parse.Output);
            }
            finally { Directory.Delete(root, true); }
        }

        static (int Exit, string Output, string Error) Run(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(typeof(Program).Assembly.Location, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            }))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    throw new System.TimeoutException("CLI did not exit: " + arguments);
                }
                return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
            }
        }
    }
}
