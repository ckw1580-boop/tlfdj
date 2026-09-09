using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using S7.Net;

namespace ElectricalSim
{
    // All operations on a transport are serialized by PlcSession. Dispose aborts pending I/O.
    public interface IPlcTransport : IDisposable
    {
        Task ConnectAsync(PlcConfiguration configuration, CancellationToken cancellation);
        Task<bool[]> ReadBitsAsync(IReadOnlyList<PlcBitAddress> addresses, CancellationToken cancellation);
        Task WriteBitAsync(PlcBitAddress address, bool value, CancellationToken cancellation);
    }

    public sealed class S7PlcTransport : IPlcTransport
    {
        private Plc client;
        public Task ConnectAsync(PlcConfiguration configuration, CancellationToken cancellation)
        {
            client = new Plc(configuration.Cpu == SiemensCpu.S71200 ? CpuType.S71200 : CpuType.S71500,
                configuration.Ip, configuration.Port, configuration.Rack, configuration.Slot)
                { ReadTimeout = configuration.TimeoutMs, WriteTimeout = configuration.TimeoutMs };
            return client.OpenAsync(cancellation);
        }
        private static DataType Area(PlcBitAddress address) => address.Area == 'D' ? DataType.DataBlock :
            address.Area == 'M' ? DataType.Memory : DataType.Output;
        public async Task<bool[]> ReadBitsAsync(IReadOnlyList<PlcBitAddress> addresses, CancellationToken cancellation)
        {
            // Cache each byte for this sample so adjacent bits come from the same PLC response.
            var bytes = new Dictionary<string, byte>();
            var result = new bool[addresses.Count];
            for (var i = 0; i < addresses.Count; i++)
            {
                var a = addresses[i];
                var key = a.Area + ":" + a.Db + ":" + a.Offset;
                if (!bytes.TryGetValue(key, out var value))
                {
                    var sample = await client.ReadBytesAsync(Area(a), a.Db, a.Offset, 1, cancellation).ConfigureAwait(false);
                    value = sample[0];
                    bytes.Add(key, value);
                }
                result[i] = (value & (1 << a.Bit)) != 0;
            }
            return result;
        }
        public Task WriteBitAsync(PlcBitAddress address, bool value, CancellationToken cancellation)
        {
            if (address.Area == 'Q') throw new ArgumentException("不允许写入真实 Q 输出。");
            // S7 bit-write request; never a read-modify-write of an entire shared byte.
            return client.WriteBitAsync(Area(address), address.Db, address.Offset, address.Bit, value, cancellation);
        }
        public void Dispose() => client?.Close();
    }
}
