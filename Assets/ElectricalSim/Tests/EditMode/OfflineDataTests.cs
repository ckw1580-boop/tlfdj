using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ElectricalSim.Tests
{
    public sealed class OfflineDataTests
    {
        private static string ExamRoot => Path.GetFullPath("Assets/StreamingAssets/OfflineData/Examine");

        [Test]
        public void AllFourOriginalExamPackagesLoadWithQuestionsAndFaults()
        {
            var packages = OfflineExamCatalog.LoadAll(ExamRoot);
            Assert.That(packages.Select(item => item.PackageId), Is.EquivalentTo(new[] { "A", "B", "C", "D" }));
            foreach (var package in packages)
            {
                Assert.That(package.WiringRules.Count, Is.GreaterThan(40), package.PackageId + " wiring rules");
                Assert.That(package.DebugRules.Count, Is.GreaterThan(20), package.PackageId + " debug rules");
                Assert.That(package.Faults.Count, Is.EqualTo(3), package.PackageId + " port faults");
                Assert.That(package.Duration.TotalHours, Is.EqualTo(2d).Within(0.001d));
            }
        }

        [Test]
        public void WiringConditionsAreMappedToQualifiedPorts()
        {
            var package = OfflineExamCatalog.LoadPackage(Path.Combine(ExamRoot, "A"));
            var condition = package.WiringRules.SelectMany(item => item.RequiredConnections).First();
            Assert.That(condition.A, Does.Contain("."));
            Assert.That(condition.B, Does.Contain("."));
        }

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
        public void LocalSessionRoundTripsWithoutNetworkState()
        {
            var root = Path.Combine(Path.GetTempPath(), "ElectricalSimTests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LocalSessionStore(root);
                var source = new ExamSessionRecord { PackageId = "A", Score = 88f, Submitted = true };
                var path = store.SaveExam(source);
                var loaded = store.LoadExam(path);
                Assert.That(loaded.PackageId, Is.EqualTo("A"));
                Assert.That(loaded.Score, Is.EqualTo(88f));
                Assert.That(loaded.Submitted, Is.True);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
