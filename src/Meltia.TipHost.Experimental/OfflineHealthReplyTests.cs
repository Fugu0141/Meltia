// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// Explicit T3f test-only responses over a REAL Windows Named Pipe (no TSF registration).
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Meltype.Tip;

namespace Meltia.TipHost.Experimental;

internal static class OfflineHealthReplyTests
{
    internal static int Run()
    {
        // These are complete response messages, not JSON-property mocking.
        // The same OfflineProbe.IsHealthy() used by the supervisor reads them
        // from a live pipe, including deliberately malformed server messages.
        (string Name, string Reply, bool Healthy)[] cases =
        {
            ("valid v1 hello", """{"version":1,"active":true,"consumed":false,"commits":[],"view":null}""", true),
            ("missing version", """{"active":true,"consumed":false,"commits":[],"view":null}""", false),
            ("missing active", """{"version":1,"consumed":false,"commits":[],"view":null}""", false),
            ("missing consumed", """{"version":1,"active":true,"commits":[],"view":null}""", false),
            ("missing commits", """{"version":1,"active":true,"consumed":false,"view":null}""", false),
            ("missing view", """{"version":1,"active":true,"consumed":false,"commits":[]}""", false),
            ("version string", """{"version":"1","active":true,"consumed":false,"commits":[],"view":null}""", false),
            ("version fraction", """{"version":1.5,"active":true,"consumed":false,"commits":[],"view":null}""", false),
            ("wrong version", """{"version":2,"active":true,"consumed":false,"commits":[],"view":null}""", false),
            ("active string", """{"version":1,"active":"true","consumed":false,"commits":[],"view":null}""", false),
            ("inactive", """{"version":1,"active":false,"consumed":false,"commits":[],"view":null}""", false),
            ("consumed number", """{"version":1,"active":true,"consumed":0,"commits":[],"view":null}""", false),
            ("consumed true", """{"version":1,"active":true,"consumed":true,"commits":[],"view":null}""", false),
            ("commits object", """{"version":1,"active":true,"consumed":false,"commits":{},"view":null}""", false),
            ("commits nonempty", """{"version":1,"active":true,"consumed":false,"commits":[{}],"view":null}""", false),
            ("view object", """{"version":1,"active":true,"consumed":false,"commits":[],"view":{}}""", false),
            ("nonobject reply", """[]""", false),
            ("null reply", """null""", false),
            ("invalid JSON", """{"version":""", false),
        };

        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value ??
                throw new InvalidOperationException("Missing user SID.");
            var endpoint = MeltiaTipIdentity.PipePrefix + sid;
            foreach (var sample in cases)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(9));
                using var server = new NamedPipeServerStream(endpoint, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                var serverTask = RespondOnceAsync(server, sample.Reply, timeout.Token);
                // Calling the production health probe must not leak KeyNotFoundException
                // or turn an invalid hello into a supervisor-wide fatal exception.
                bool actual = OfflineProbe.IsHealthy();
                serverTask.GetAwaiter().GetResult();
                if (actual != sample.Healthy)
                    throw new InvalidDataException(
                        $"'{sample.Name}': expected healthy={sample.Healthy}, actual={actual}.");
            }
            Console.WriteLine($"PASS T3f: {cases.Length} real-pipe hello reply cases (missing/wrong types fail closed).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL T3f health reply contract: " + ex);
            return 1;
        }
    }

    private static async Task RespondOnceAsync(NamedPipeServerStream pipe, string reply, CancellationToken ct)
    {
        await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
        var buffer = new byte[8192];
        var size = await pipe.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
        if (size <= 0) throw new IOException("Health probe disconnected before sending hello.");
        using var request = JsonDocument.Parse(buffer.AsMemory(0, size));
        if (request.RootElement.GetProperty("op").GetString() != "hello")
            throw new InvalidDataException("Health checker sent an operation other than hello.");

        await pipe.WriteAsync(Encoding.UTF8.GetBytes(reply).AsMemory(), ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);

        // Keep the reply pipe alive until IsHealthy disposes its client stream,
        // otherwise closing this server can race the client's ReadAsync.
        try
        {
            while (await pipe.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false) != 0) { }
        }
        catch (IOException) { /* Expected when the client closes the connection. */ }
    }
}
