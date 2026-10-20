// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// T3c-A: an isolated real Windows Named Pipe server, NOT the production IME host.
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Meltype.Tip;

if (!OperatingSystem.IsWindows() || args.Length != 1)
{
    Console.Error.WriteLine("Usage (Windows only): Meltia.TipIpcFixture <ready-marker-file>");
    return 2;
}

const string Active = "{\"active\":true,\"consumed\":false,\"commits\":[],\"view\":null}";
var identity = WindowsIdentity.GetCurrent().User?.Value
    ?? throw new InvalidOperationException("A Windows user SID is required.");
var endpoint = MeltiaTipIdentity.PipePrefix + identity;
var cases = new (string Op, bool Active, bool Dispatched)[][]
{
    new[] {
        ("hello", true, true),
        ("key", true, true),
        ("key", false, false),  // Wrong version revokes previous hello
        ("key", false, false),  // Valid version alone cannot recover it
        ("hello", true, true),
        ("key", true, true)
    },
    new[] {
        ("key", false, false),  // Connection B must not inherit A's hello
        ("hello", true, true),
        ("key", true, true)
    }
};

try
{
    for (var connection = 0; connection < cases.Length; connection++)
    {
        var options = PipeOptions.Asynchronous |
            (connection == 0 ? PipeOptions.FirstPipeInstance : PipeOptions.None);
        using var pipe = new NamedPipeServerStream(
            endpoint, PipeDirection.InOut, 1, PipeTransmissionMode.Message, options);
        // The marker is created only after the first endpoint is really bound.
        if (connection == 0) File.WriteAllText(args[0], "ready", Encoding.UTF8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await pipe.WaitForConnectionAsync(timeout.Token);
        var gate = new MeltiaTipWire(); // One fresh gate for each accepted connection.

        foreach (var expected in cases[connection])
        {
            using var message = new MemoryStream();
            var buffer = new byte[8192];
            do
            {
                var read = await pipe.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (read == 0) throw new IOException("Native client disconnected too soon.");
                message.Write(buffer, 0, read);
                if (message.Length > 64 * 1024) throw new IOException("Oversized request.");
            }
            while (!pipe.IsMessageComplete);

            var request = Encoding.UTF8.GetString(message.ToArray());
            using (var parsed = JsonDocument.Parse(request))
            {
                var op = parsed.RootElement.GetProperty("op").GetString();
                if (op != expected.Op) throw new InvalidDataException(
                    $"Unexpected operation on connection {connection + 1}: {op}");
            }

            var calls = 0;
            var reply = gate.Process(request, payload =>
            {
                calls++;
                using var parsed = JsonDocument.Parse(payload);
                if (parsed.RootElement.GetProperty("op").GetString() == "key")
                {
                    if (parsed.RootElement.GetProperty("sid").GetString() != "t3c" ||
                        parsed.RootElement.GetProperty("before").GetString() != "日本語")
                        throw new InvalidDataException("UTF-8 key payload was corrupted.");
                }
                // Synthetic dispatcher: verifies framing and handshake, NOT conversion.
                return Active;
            });
            if ((calls == 1) != expected.Dispatched || gate.Handshaken != expected.Active)
                throw new InvalidDataException("Unexpected dispatch/handshake state.");

            var expectedWireReply = expected.Active
                ? "{\"version\":" + MeltiaTipIdentity.ProtocolVersion + "," + Active[1..]
                : MeltiaTipWire.Inactive;
            if (!string.Equals(reply, expectedWireReply, StringComparison.Ordinal))
                throw new InvalidDataException("Unexpected v1 response framing.");

            var bytes = Encoding.UTF8.GetBytes(reply);
            await pipe.WriteAsync(bytes.AsMemory(), timeout.Token);
            await pipe.FlushAsync(timeout.Token);
        }
    }
    Console.WriteLine("PASS T3c-A native/C# Named Pipe v1: 2 connections, 9 request/reply pairs.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL T3c-A: " + ex);
    return 1;
}
