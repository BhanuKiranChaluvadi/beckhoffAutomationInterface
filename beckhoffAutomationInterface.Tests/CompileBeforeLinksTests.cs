using System;
using System.Runtime.InteropServices;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    /// <summary>A failed compile before links.xml must surface as PlcCompileException, so
    /// SyncLinks can run the build that prints the compiler errors (CompileProject itself
    /// reports only E_FAIL).</summary>
    public class CompileBeforeLinksTests
    {
        [Fact]
        public void ComFailure_BecomesPlcCompileException_WithTheCause()
        {
            var cause = new COMException("Error HRESULT E_FAIL has been returned from a call to a COM component.", unchecked((int)0x80004005));

            var ex = Assert.Throws<PlcCompileException>(() => VariableLinkEngine.CompileBeforeLinks(() => { throw cause; }));

            Assert.Same(cause, ex.InnerException);
            Assert.StartsWith("PLC compilation failed before applying links.xml; mappings were not changed", ex.Message);
            Assert.Contains("E_FAIL", ex.Message);
        }

        [Fact]
        public void PlcCompileException_IsStillAnInvalidOperationException()
        {
            // MachineBuilder reports InvalidOperationException as "ERROR (build)": that must not change.
            var ex = Assert.Throws<PlcCompileException>(() => VariableLinkEngine.CompileBeforeLinks(() => { throw new InvalidCastException(); }));

            Assert.IsAssignableFrom<InvalidOperationException>(ex);
        }

        [Fact]
        public void SuccessfulCompile_ThrowsNothing()
        {
            bool compiled = false;

            VariableLinkEngine.CompileBeforeLinks(() => compiled = true);

            Assert.True(compiled);
        }
    }
}
