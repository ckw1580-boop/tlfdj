using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ElectricalSim
{
    public sealed class PlcSession : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<IPlcTransport> factory;
        private readonly SemaphoreSlim wake = new SemaphoreSlim(0, 1);
        private PlcConfiguration configuration;
        private PlcConnectionState state;
        private string error = "";
        private bool simulate, stop, resetInputs;
        private int generation;
        private bool[] inputs = new bool[14], outputs = new bool[10], remoteInputs = new bool[14];
        private long sampleTime;
        private Task worker = Task.CompletedTask;
        public PlcSession(PlcConfiguration config, Func<IPlcTransport> transportFactory = null)
        { configuration = config.Clone(); factory = transportFactory ?? (() => new S7PlcTransport()); }
        public PlcConfiguration Configuration { get { lock (gate) return configuration.Clone(); } }
        public PlcConnectionState State { get { lock (gate) return state; } }
        public string Error { get { lock (gate) return error; } }
        public bool IsBusy => State == PlcConnectionState.Connected || State == PlcConnectionState.Connecting || State == PlcConnectionState.Disconnecting;
        public bool[] RemoteInputs { get { lock (gate) return (bool[])remoteInputs.Clone(); } }
        public bool[] RemoteOutputs { get { lock (gate) return (bool[])outputs.Clone(); } }
        public bool HasFreshSample { get { lock (gate) return state == PlcConnectionState.Connected && sampleTime != 0 &&
            (Stopwatch.GetTimestamp() - sampleTime) * 1000d / Stopwatch.Frequency < configuration.PollIntervalMs + configuration.TimeoutMs; } }
        public void Configure(PlcConfiguration config)
        {
            var copy = config.Clone(); copy.Validate();
            lock (gate)
            {
                if (!worker.IsCompleted || IsBusy) throw new InvalidOperationException("请先断开 PLC 后修改配置。");
                if (copy.DeviceId != configuration.DeviceId) throw new ArgumentException("设备标识不能改变。");
                configuration = copy; error = ""; state = PlcConnectionState.Disconnected;
            }
        }
        public void Connect()
        {
            lock (gate)
            {
                if (!worker.IsCompleted || IsBusy) throw new InvalidOperationException("PLC 连接操作尚未结束。");
                configuration.Validate(true);
                stop = false; generation++; sampleTime = 0; error = "";
                Array.Clear(outputs, 0, outputs.Length);
                state = PlcConnectionState.Connecting;
                var config = configuration.Clone();
                worker = Task.Run(() => Run(config));
            }
        }
        public void SetSimulation(bool enabled, bool[] values)
        {
            lock (gate)
            {
                if (simulate != enabled) { if (!enabled) resetInputs = true; simulate = enabled; generation++; sampleTime = 0; Wake(); }
                inputs = enabled && values != null ? (bool[])values.Clone() : new bool[14];
            }
        }
        public Task DisconnectAsync()
        {
            lock (gate)
            {
                stop = true; simulate = false; generation++; sampleTime = 0;
                Wake();
                Array.Clear(outputs, 0, outputs.Length);
                if (!worker.IsCompleted) state = PlcConnectionState.Disconnecting;
                else if (state != PlcConnectionState.Faulted) state = PlcConnectionState.Disconnected;
                return worker;
            }
        }
        private void Wake() { if (wake.CurrentCount == 0) wake.Release(); }
        private async Task Deadline(IPlcTransport connection, Func<CancellationToken, Task> action, int timeout)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var operation = action(cancellation.Token);
                var timer = Task.Delay(timeout, cancellation.Token);
                if (await Task.WhenAny(operation, timer).ConfigureAwait(false) != operation)
                {
                    cancellation.Cancel();
                    connection.Dispose();
                    _ = operation.ContinueWith(t => { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException("PLC 通信超时。请检查网络后手动重新连接。");
                }
                cancellation.Cancel();
                await operation.ConfigureAwait(false);
            }
        }
        private async Task Run(PlcConfiguration config)
        {
            var inputAddresses = config.Inputs.Select(p => PlcBitAddress.Parse(p.Address, true)).ToArray();
            var outputAddresses = config.Outputs.Select(p => PlcBitAddress.Parse(p.Address)).ToArray();
            bool[] written = null;
            var inputsOwned = false;
            IPlcTransport connection = null;
            try
            {
                connection = factory();
                await Deadline(connection, ct => connection.ConnectAsync(config, ct), config.TimeoutMs).ConfigureAwait(false);
                while (true)
                {
                    bool running, stopping, clear; int epoch; bool[] desired;
                    lock (gate) { running = simulate; stopping = stop; clear = resetInputs; resetInputs = false; epoch = generation; desired = (bool[])inputs.Clone(); }
                    if ((!running || stopping || clear) && inputsOwned)
                    {
                        await Deadline(connection, async ct =>
                        {
                            foreach (var address in inputAddresses)
                            { ct.ThrowIfCancellationRequested(); await connection.WriteBitAsync(address, false, ct).ConfigureAwait(false); }
                        }, config.TimeoutMs).ConfigureAwait(false);
                        written = null; inputsOwned = false;
                    }
                    if (stopping) break;
                    bool[] sampledOutputs = null, sampledInputs = null;
                    await Deadline(connection, async ct =>
                    {
                        if (running)
                        {
                            for (var i = 0; i < desired.Length; i++)
                            {
                                ct.ThrowIfCancellationRequested();
                                lock (gate) { if (epoch != generation || stop) return; }
                                if (written == null || written[i] != desired[i])
                                {
                                    inputsOwned = true;
                                    await connection.WriteBitAsync(inputAddresses[i], desired[i], ct).ConfigureAwait(false);
                                }
                            }
                            written = desired;
                        }
                        ct.ThrowIfCancellationRequested();
                        sampledOutputs = await connection.ReadBitsAsync(outputAddresses, ct).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        sampledInputs = await connection.ReadBitsAsync(inputAddresses, ct).ConfigureAwait(false);
                        if (sampledOutputs.Length != 10 || sampledInputs.Length != 14) throw new InvalidOperationException("PLC 返回的点位数量不正确。");
                    }, config.TimeoutMs).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (epoch == generation && !stop && sampledOutputs != null)
                        {
                            outputs = sampledOutputs; remoteInputs = sampledInputs; sampleTime = Stopwatch.GetTimestamp();
                            state = PlcConnectionState.Connected;
                        }
                    }
                    await wake.WaitAsync(config.PollIntervalMs).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                lock (gate) { error = exception.Message; state = PlcConnectionState.Faulted; }
            }
            finally
            {
                try { connection?.Dispose(); } catch { /* The I/O error above is the useful diagnostic. */ }
                lock (gate)
                {
                    sampleTime = 0; Array.Clear(outputs, 0, outputs.Length);
                    if (state != PlcConnectionState.Faulted) state = PlcConnectionState.Disconnected;
                }
            }
        }
        public void Dispose() { _ = DisconnectAsync(); }
    }
}
