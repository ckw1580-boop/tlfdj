using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ElectricalSim.Tests
{
    public sealed class OfflineDataTests
    {
        [Test]
        public void LocalPlcScansNormallyOpenAndNormallyClosedContacts()
        {
            var plc = new LocalPlcRuntime();
            plc.LoadProgram(new[]
            {
                new PlcRung
                {
                    CoilAddress = "Q0.0",
                    Contacts = { new PlcContact { Address = "I0.0" }, new PlcContact { Address = "I0.1", NormallyClosed = true } }
                }
            });
            plc.Write("I0.0", true);
            plc.ScanOnce();
            Assert.That(plc.Read("Q0.0"), Is.True);
            plc.Write("I0.1", true);
            plc.ScanOnce();
            Assert.That(plc.Read("Q0.0"), Is.False);
        }

        [Test]
        public void CaptureAndProjectDirectoriesRemainAvailableWithoutExamStorage()
        {
            var root = Path.Combine(Path.GetTempPath(), "ElectricalSimTests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LocalSessionStore(root);
                Assert.That(Directory.Exists(store.ProjectsDirectory), Is.True);
                Assert.That(Directory.Exists(store.CapturesDirectory), Is.True);
                Assert.That(Directory.Exists(store.RecordingsDirectory), Is.True);
                Assert.That(Directory.Exists(Path.Combine(root, "成绩")), Is.False);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
