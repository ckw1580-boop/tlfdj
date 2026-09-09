using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests
{
    public sealed class PlcTests
    {
        private sealed class Transport : IPlcTransport
        {
            public readonly ConcurrentDictionary<string, bool> Bits = new ConcurrentDictionary<string, bool>();
            public readonly ConcurrentQueue<string> Writes = new ConcurrentQueue<string>();
            public volatile bool Fail, Hang, Disposed;
            public TaskCompletionSource<bool> HeldRead;
            public int Reads;
            public Task ConnectAsync(PlcConfiguration c, CancellationToken ct) => Task.CompletedTask;
            public async Task<bool[]> ReadBitsAsync(IReadOnlyList<PlcBitAddress> addresses, CancellationToken ct)
            {
                Interlocked.Increment(ref Reads);
                if (Fail) throw new InvalidOperationException("模拟网络中断");
                if (Hang) await new TaskCompletionSource<bool>().Task;
                var result = addresses.Select(a => Bits.TryGetValue(a.ToString(), out var value) && value).ToArray();
                if (HeldRead != null) await HeldRead.Task;
                return result;
            }
            public Task WriteBitAsync(PlcBitAddress a, bool value, CancellationToken ct)
            { Bits[a.ToString()] = value; Writes.Enqueue(a + "=" + value); return Task.CompletedTask; }
            public void Dispose() { Disposed = true; }
        }
        private static PlcConfiguration Config(string id = "PLC_1")
        { var c = PlcConfiguration.Create(id); c.Ip = "127.0.0.1"; c.PollIntervalMs = 20; c.TimeoutMs = 200; return c; }
        private static async Task Until(Func<bool> predicate)
        {
            var watch = Stopwatch.StartNew();
            while (!predicate() && watch.ElapsedMilliseconds < 2500) await Task.Delay(10);
            Assert.That(predicate(), Is.True, "等待 PLC 状态超时");
        }
        [TestCase("m001.7", "M1.7")]
        [TestCase("DB002.DBX10.3", "DB2.DBX10.3")]
        [TestCase("Q0.0", "Q0.0")]
        public void AddressesHaveCanonicalBitIdentity(string input, string expected)
            => Assert.That(PlcBitAddress.Parse(input).ToString(), Is.EqualTo(expected));
        [TestCase("I0.0")][TestCase("M0.8")][TestCase("DB0.DBX0.0")][TestCase("DB1.DBW0")]
        [TestCase("M-1.0")][TestCase("M999999999999.0")][TestCase("DB65536.DBX0.0")]
        public void UnsupportedAddressesAreRejected(string address)
            => Assert.Throws<ArgumentException>(() => PlcBitAddress.Parse(address));
        [Test]
        public void WriteMappingsCannotTargetQOrAliasTheSameBit()
        {
            Assert.Throws<ArgumentException>(() => PlcBitAddress.Parse("Q0.0", true));
            var c = Config(); c.Inputs[1].Address = "m00.0";
            Assert.Throws<ArgumentException>(() => c.Validate());
            c = Config(); c.Inputs.RemoveAt(0); Assert.Throws<ArgumentException>(() => c.Validate());
        }
        [Test] public void ReadOnlyThenBidirectionalThenClearPreservesUnmappedBits() => Task.Run(ReadOnlyThenBidirectional).GetAwaiter().GetResult();
        private async Task ReadOnlyThenBidirectional()
        {
            var transport = new Transport(); transport.Bits["M1.6"] = true; transport.Bits["Q0.0"] = true;
            using (var session = new PlcSession(Config(), () => transport))
            {
                session.Connect(); await Until(() => session.HasFreshSample);
                Assert.That(transport.Writes, Is.Empty);
                Assert.That(session.RemoteOutputs[0], Is.True);
                var input = new bool[14]; input[0] = true; input[13] = true;
                session.SetSimulation(true, input);
                await Until(() => session.HasFreshSample && session.RemoteInputs[0]);
                Assert.That(transport.Bits["M1.5"], Is.True);
                Assert.Throws<InvalidOperationException>(() => session.Configure(Config()));
                session.SetSimulation(false, null);
                Assert.That(session.HasFreshSample, Is.False);
                await Until(() => transport.Bits.TryGetValue("M0.0", out var value) && !value);
                Assert.That(transport.Bits["M1.6"], Is.True);
                Assert.That(transport.Writes.All(w => !w.StartsWith("Q") && !w.StartsWith("M1.6")), Is.True);
                await session.DisconnectAsync();
                Assert.That(session.State, Is.EqualTo(PlcConnectionState.Disconnected));
                Assert.That(transport.Disposed, Is.True);
            }
        }
        [Test] public void TwoSessionsAreIndependentAndFaultRequiresManualReconnect() => Task.Run(TwoSessionsAreIndependent).GetAwaiter().GetResult();
        private async Task TwoSessionsAreIndependent()
        {
            var a = new Transport(); var b = new Transport(); a.Bits["Q0.0"] = true;
            using (var one = new PlcSession(Config(), () => a))
            using (var two = new PlcSession(Config("PLC_2"), () => b))
            {
                one.Connect(); two.Connect(); await Until(() => one.HasFreshSample && two.HasFreshSample);
                Assert.That(two.RemoteOutputs[0], Is.False);
                a.Fail = true; await Until(() => one.State == PlcConnectionState.Faulted);
                Assert.That(one.HasFreshSample, Is.False); Assert.That(one.RemoteOutputs[0], Is.False);
                Assert.That(two.HasFreshSample, Is.True);
                a.Fail = false; await Task.Delay(100); Assert.That(one.State, Is.EqualTo(PlcConnectionState.Faulted));
                await one.DisconnectAsync(); one.Connect(); await Until(() => one.HasFreshSample);
                await one.DisconnectAsync(); await two.DisconnectAsync();
            }
        }
        [Test] public void TimeoutAbortsTransportEvenWhenItIgnoresCancellation() => Task.Run(TimeoutAbortsTransport).GetAwaiter().GetResult();
        private async Task TimeoutAbortsTransport()
        {
            var transport = new Transport { Hang = true };
            using (var session = new PlcSession(Config(), () => transport))
            {
                session.Connect(); await Until(() => session.State == PlcConnectionState.Faulted);
                Assert.That(session.Error, Does.Contain("超时")); Assert.That(transport.Disposed, Is.True);
                Assert.That(session.HasFreshSample, Is.False);
            }
        }
        [Test] public void DisconnectRejectsLateOutputAndClearsOnlyOwnedInputBits() => Task.Run(DisconnectRejectsLateOutput).GetAwaiter().GetResult();
        private async Task DisconnectRejectsLateOutput()
        {
            var transport = new Transport(); transport.Bits["Q0.0"] = true;
            using (var session = new PlcSession(Config(), () => transport))
            {
                session.SetSimulation(true, Enumerable.Repeat(true, 14).ToArray());
                transport.HeldRead = new TaskCompletionSource<bool>(); session.Connect();
                await Until(() => transport.Reads > 0);
                var done = session.DisconnectAsync();
                transport.HeldRead.SetResult(true); await done;
                Assert.That(session.HasFreshSample, Is.False); Assert.That(session.RemoteOutputs.All(x => !x), Is.True);
                Assert.That(PlcConfiguration.InputTerminals.All(a => !transport.Bits[a]), Is.True);
            }
        }
        [Test]
        public void PoweredWiringControlsInputAndOutputWithoutBridgingTheTwoPlcs()
        {
            var graph = new CircuitGraph();
            graph.RegisterDevice(new ElectricalDeviceRuntime("DC", ElectricalDeviceKind.PowerSource, new[] { "DC_POSITIVE", "DC_NEGATIVE" }) { IsDcSource = true });
            var a = new PlcDeviceRuntime("PLC_1"); var b = new PlcDeviceRuntime("PLC_2"); graph.RegisterDevice(a); graph.RegisterDevice(b);
            var lamp = ElectricalDeviceRuntime.CreatePanel(PanelDeviceCatalog.Create().Single(d => d.Id == "HL1")); graph.RegisterDevice(lamp);
            foreach (var terminal in new[] { "L+", "3L+", "M0.0" }) graph.AddWire("DC.DC_POSITIVE", a.Port(terminal), Color.red);
            foreach (var terminal in new[] { "M", "1M", "3M-" }) graph.AddWire("DC.DC_NEGATIVE", a.Port(terminal), Color.blue);
            graph.AddWire(a.Port("Q0.0"), "HL1.L", Color.red); graph.AddWire("DC.DC_NEGATIVE", "HL1.N", Color.blue);
            a.SetOutputs(true, new[] { true, false, false, false, false, false, false, false, false, false }); graph.Solve();
            Assert.That(a.InputStates[0], Is.True); Assert.That(lamp.IsActive, Is.True); Assert.That(b.IsActive, Is.False);
            var supply = graph.Wires.Single(w => w.EndPort == a.Port("3M-")); graph.RemoveWire(supply.Id); graph.Solve();
            Assert.That(lamp.IsActive, Is.False);
            graph.RemoveWire(graph.Wires.Single(w => w.EndPort == a.Port("1M")).Id); graph.Solve();
            Assert.That(a.InputStates[0], Is.False);
        }
        [Test] public void TimedOutResponseCannotUseOrPublishIntoNewConnection() => Task.Run(async () =>
        {
            var first = new Transport { HeldRead = new TaskCompletionSource<bool>() }; first.Bits["Q0.0"] = true;
            var second = new Transport(); var attempt = 0;
            using (var session = new PlcSession(Config(), () => ++attempt == 1 ? first : second))
            {
                session.Connect(); await Until(() => session.State == PlcConnectionState.Faulted);
                await session.DisconnectAsync(); session.Connect(); await Until(() => session.HasFreshSample);
                first.HeldRead.SetResult(true); await Task.Delay(80);
                Assert.That(first.Reads, Is.EqualTo(1), "cancelled cycle must not continue to the next read");
                Assert.That(session.RemoteOutputs[0], Is.False); Assert.That(session.State, Is.EqualTo(PlcConnectionState.Connected));
                await session.DisconnectAsync();
            }
        }).GetAwaiter().GetResult();
        [Test] public void LongPollingIntervalKeepsSampleAndDoesNotDelayDisconnect() => Task.Run(async () =>
        {
            var c = Config(); c.PollIntervalMs = 10000; var transport = new Transport();
            using (var session = new PlcSession(c, () => transport))
            {
                session.Connect(); await Until(() => session.HasFreshSample); await Task.Delay(300);
                Assert.That(session.HasFreshSample, Is.True);
                var watch = Stopwatch.StartNew(); await session.DisconnectAsync();
                Assert.That(watch.ElapsedMilliseconds, Is.LessThan(1000));
            }
        }).GetAwaiter().GetResult();
    }
}
