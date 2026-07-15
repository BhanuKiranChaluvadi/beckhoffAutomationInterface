using System.Collections.Generic;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>
    /// Task 6: pure translation of TwinCAT build-error locations back to .st source,
    /// against the error shapes confirmed empirically in Task 4 (2026-07-14).
    /// </summary>
    public class ErrorLocationResolverTests
    {
        static Dictionary<string, StPouSource> Index()
        {
            var fb = new StPouSource("FB_Motor", PouKind.FunctionBlock, null, "decl", "impl")
            {
                SourceFileName = "FB_Motor.st",
                RelativeFolder = "Lib/Motors",
                DeclarationStartLine = 2,
                ImplementationStartLine = 6,
            };
            var method = new StPouSource("Init", PouKind.Method, "FB_Motor", "decl", "impl")
            {
                SourceFileName = "FB_Motor.st",
                RelativeFolder = "Lib/Motors",
                DeclarationStartLine = 10,
                ImplementationStartLine = 14,
            };
            var property = new StPouSource("Setpoint", PouKind.Property, "FB_Motor", "decl", null, "LREAL", "get", "set")
            {
                SourceFileName = "FB_Motor.st",
                RelativeFolder = "Lib/Motors",
                DeclarationStartLine = 20,
                GetStartLine = 22,
                SetStartLine = 25,
            };
            var gvl = new StPouSource("GVL_Config", PouKind.Gvl, null, "decl", null)
            {
                SourceFileName = "GVL_Config.st",
                DeclarationStartLine = 1,
            };
            return ErrorLocationResolver.BuildIndex(new[] { fb, method, property, gvl });
        }

        [Fact]
        public void FbImplementationError_MapsToImplStartPlusOffset()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\FB_Motor.TcPOU (Impl)", 2, Index());

            Assert.True(loc.Mapped);
            Assert.Equal("Lib/Motors/FB_Motor.st", loc.Path);
            Assert.Equal(7, loc.Line); // impl starts 6, reported line 2 -> 6 + (2-1)
        }

        [Fact]
        public void MethodImplementationError_MapsIntoThatMethod()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\FB_Motor.TcPOU@Init (Impl)", 3, Index());

            Assert.True(loc.Mapped);
            Assert.Equal("Lib/Motors/FB_Motor.st", loc.Path);
            Assert.Equal(16, loc.Line); // method impl starts 14 -> 14 + (3-1)
        }

        [Fact]
        public void PropertyGetError_MapsToGetAccessorBody()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\FB_Motor.TcPOU@Setpoint.Get (Impl)", 1, Index());

            Assert.True(loc.Mapped);
            Assert.Equal(22, loc.Line);
        }

        [Fact]
        public void PropertySetError_MapsToSetAccessorBody()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\FB_Motor.TcPOU@Setpoint.Set (Impl)", 2, Index());

            Assert.True(loc.Mapped);
            Assert.Equal(26, loc.Line); // set body starts 25 -> 25 + (2-1)
        }

        [Fact]
        public void GvlError_IsDeclarationRelative_WholeFile()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\GVL_Config.TcGVL", 3, Index());

            Assert.True(loc.Mapped);
            Assert.Equal("GVL_Config.st", loc.Path);
            Assert.Equal(3, loc.Line); // decl starts 1 -> file line verbatim
        }

        [Fact]
        public void DeclarationSectionError_MapsFromDeclarationStart()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\FB_Motor.TcPOU (Decl)", 2, Index());

            Assert.True(loc.Mapped);
            Assert.Equal(3, loc.Line); // decl starts 2 -> 2 + (2-1)
        }

        [Fact]
        public void ProjectLevelError_EmptyFileName_FallsBackUnmapped()
        {
            var loc = ErrorLocationResolver.Resolve("", 0, Index());

            Assert.False(loc.Mapped);
        }

        [Fact]
        public void UnknownObject_FallsBackUnmapped_PreservingRawValues()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\proj\FB_NotOurs.TcPOU (Impl)", 9, Index());

            Assert.False(loc.Mapped);
            Assert.Equal(@"C:\proj\FB_NotOurs.TcPOU (Impl)", loc.Path);
            Assert.Equal(9, loc.Line);
        }

        [Fact]
        public void LibraryFilePath_NotMatchingExportedShape_FallsBackUnmapped()
        {
            var loc = ErrorLocationResolver.Resolve(@"C:\TwinCAT\3.1\Components\Tc2_Standard.library", 12, Index());

            Assert.False(loc.Mapped);
        }
    }
}
