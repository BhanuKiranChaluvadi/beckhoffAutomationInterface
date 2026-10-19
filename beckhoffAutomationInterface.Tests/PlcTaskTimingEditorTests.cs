using System.Xml.Linq;
using BeckhoffAutomationInterface.Sync;
using Xunit;

namespace BeckhoffAutomationInterface.Tests
{
    public class PlcTaskTimingEditorTests
    {
        [Fact]
        public void RepairsCachedTimingAndPreservesTaskIdentityAndCalls()
        {
            string root = Path.Combine(Path.GetTempPath(), "bai-task-cache-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            try
            {
                string project = Path.Combine(root,"M.tsproj"), task = Path.Combine(root,"PlcTask.TcTTO");
                File.WriteAllText(project,"<TcSmProject><Project><Plc><Project Name='PLC' PrjFilePath='PLC.plcproj'/></Plc></Project></TcSmProject>");
                File.WriteAllText(Path.Combine(root,"PLC.plcproj"),"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'><ItemGroup><Compile Include='PlcTask.TcTTO'/></ItemGroup></Project>");
                File.WriteAllText(task,"<TcPlcObject><Task Name='PlcTask' Id='{original}'><CycleTime>10000</CycleTime><Priority>20</Priority><PouCall><Name>MAIN</Name></PouCall></Task></TcPlcObject>");
                var desired = TaskManifestParser.Parse(XDocument.Parse("<Tasks><Task Name='PlcTask' CycleTimeUs='50000' Priority='21'><Call>MAIN</Call></Task></Tasks>"),"test");
                var change = Assert.Single(PlcTaskTimingEditor.Changes(project,"PLC",desired));
                change.Value.Save(change.Key);
                Assert.Empty(PlcTaskTimingEditor.Changes(project,"PLC",desired));
                var xml = XDocument.Load(task).Root.Element("Task");
                Assert.Equal("{original}",(string)xml.Attribute("Id"));
                Assert.Equal("MAIN",(string)xml.Element("PouCall").Element("Name"));
                Assert.Equal(50000,(int)xml.Element("CycleTime"));
                Assert.Equal(21,(int)xml.Element("Priority"));
            }
            finally { Directory.Delete(root,true); }
        }
    }
}
