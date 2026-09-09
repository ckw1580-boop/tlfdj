using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ElectricalSim.Tests
{
    // Local protocol peer exercises the shipped S7.Net DLL, including Unity's System.Memory shim.
    // This verifies packets and compatibility, not real CPU firmware or electrical hardware.
    public sealed class PlcTransportWireTests
    {
        [TestCase(SiemensCpu.S71200)]
        [TestCase(SiemensCpu.S71500)]
        public void ActualDriverNegotiatesReadsAndWritesOneBit(SiemensCpu cpu)
            => Task.Run(() => Exchange(cpu)).GetAwaiter().GetResult();

        private static async Task Exchange(SiemensCpu cpu)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var writes = new List<byte[]>();
            var memory = new Dictionary<int, byte> { [0x830001] = 0x40, [0x820000] = 1, [0x84000a] = 0x80 };
            var peer = Task.Run(async () =>
            {
                using (var socket = await listener.AcceptTcpClientAsync())
                using (var stream = socket.GetStream())
                {
                    while (true)
                    {
                        var header = new byte[4];
                        if (!await ReadExactly(stream, header)) break;
                        var request = new byte[header[2] * 256 + header[3]];
                        Array.Copy(header, request, 4);
                        var body = new byte[request.Length - 4];
                        if (!await ReadExactly(stream, body)) throw new EndOfStreamException();
                        Array.Copy(body, 0, request, 4, body.Length);
                        byte[] response;
                        if (request[5] == 0xe0) { response = (byte[])request.Clone(); response[5] = 0xd0; }
                        else if (request[17] == 0xf0)
                            response = Ack(request, new byte[] { 0xf0, 0, 0, 1, 0, 1, 1, 0xe0 }, Array.Empty<byte>());
                        else
                        {
                            var bit = request[28] * 65536 + request[29] * 256 + request[30];
                            var key = request[27] * 65536 + bit / 8;
                            memory.TryGetValue(key, out var value);
                            if (request[17] == 5)
                            {
                                writes.Add(request);
                                var mask = 1 << bit % 8;
                                memory[key] = request[35] != 0 ? (byte)(value | mask) : (byte)(value & ~mask);
                                response = Ack(request, new byte[] { 5, 1 }, new byte[] { 0xff });
                            }
                            else response = Ack(request, new byte[] { 4, 1 }, new byte[] { 0xff, 4, 0, 8, value });
                        }
                        await stream.WriteAsync(response, 0, response.Length);
                    }
                }
            });
            try
            {
                using (var driver = new S7PlcTransport())
                using (var timeout = new CancellationTokenSource(5000))
                using (timeout.Token.Register(driver.Dispose))
                {
                    var c = PlcConfiguration.Create("PLC_1"); c.Cpu = cpu; c.Ip = "127.0.0.1"; c.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    await driver.ConnectAsync(c, timeout.Token);
                    await driver.WriteBitAsync(PlcBitAddress.Parse("M1.5", true), true, timeout.Token);
                    await driver.WriteBitAsync(PlcBitAddress.Parse("DB10.DBX10.0", true), true, timeout.Token);
                    var read = await driver.ReadBitsAsync(new[] { PlcBitAddress.Parse("M1.5"), PlcBitAddress.Parse("M1.6"), PlcBitAddress.Parse("Q0.0"), PlcBitAddress.Parse("DB10.DBX10.7") }, timeout.Token);
                    Assert.That(read, Is.EqualTo(new[] { true, true, true, true }));
                    await driver.WriteBitAsync(PlcBitAddress.Parse("M1.5", true), false, timeout.Token);
                    read = await driver.ReadBitsAsync(new[] { PlcBitAddress.Parse("M1.5"), PlcBitAddress.Parse("M1.6") }, timeout.Token);
                    Assert.That(read, Is.EqualTo(new[] { false, true }));
                }
                await peer;
                Assert.That(writes.Count, Is.EqualTo(3));
                foreach (var packet in writes)
                {
                    Assert.That(packet[22], Is.EqualTo(1), "S7 parameter transport size must be BIT");
                    Assert.That(packet[24], Is.EqualTo(1), "write exactly one bit");
                    Assert.That(packet[32], Is.EqualTo(3), "data transport size must be BIT");
                    Assert.That(packet[27], Is.Not.EqualTo(0x82), "never write outputs");
                }
                Assert.That(writes[1][26], Is.EqualTo(10), "DB number is preserved");
            }
            finally { listener.Stop(); }
        }
        private static async Task<bool> ReadExactly(Stream stream, byte[] bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                var count = await stream.ReadAsync(bytes, offset, bytes.Length - offset);
                if (count == 0) return false;
                offset += count;
            }
            return true;
        }
        private static byte[] Ack(byte[] request, byte[] parameters, byte[] data)
        {
            var result = new byte[19 + parameters.Length + data.Length];
            var header = new byte[] { 3, 0, 0, (byte)result.Length, 2, 0xf0, 0x80, 0x32, 3, 0, 0, request[11], request[12], 0, (byte)parameters.Length, 0, (byte)data.Length, 0, 0 };
            Array.Copy(header, result, header.Length); Array.Copy(parameters, 0, result, 19, parameters.Length); Array.Copy(data, 0, result, 19 + parameters.Length, data.Length);
            return result;
        }
    }
}
