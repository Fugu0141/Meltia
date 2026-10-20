// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Meltype.Tip;

namespace Meltia.TipHost.Experimental;

internal static class OfflineProbe
{
    internal static int Run()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Missing current user SID.");
            using var pipe = new NamedPipeClientStream(".", MeltiaTipIdentity.PipePrefix + sid,
                PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(5000);
            pipe.ReadMode = PipeTransmissionMode.Message;
            pipe.ReadTimeout = 5000;
            pipe.WriteTimeout = 5000;

            using var hello = Exchange(pipe, """{"version":1,"op":"hello","process":"notepad"}""");
            Require(hello, true, false);
            using var k = Exchange(pipe, """{"version":1,"op":"key","sid":"t3e","vk":75,"ch":107,"mods":0,"before":"","after":"","process":"notepad","composing":false}""");
            Require(k, true, true);
            using var a = Exchange(pipe, """{"version":1,"op":"key","sid":"t3e","vk":65,"ch":97,"mods":0,"before":"","after":"","process":"notepad","composing":false}""");
            Require(a, true, true);
            if (a.RootElement.GetProperty("view").GetProperty("text").GetString() != "か")
                throw new InvalidDataException("Expected か in real MeltypeSession view.");

            using var commit = Exchange(pipe, """{"version":1,"op":"commit","sid":"t3e","process":"notepad"}""");
            Require(commit, true, true);
            var commits = commit.RootElement.GetProperty("commits");
            if (commits.GetArrayLength() != 1 ||
                commits[0].GetProperty("text").GetString() != "か" ||
                commits[0].GetProperty("deleteBefore").GetInt32() != 0)
                throw new InvalidDataException("Incorrect committed kana or edit length.");
            using var close = Exchange(pipe, """{"version":1,"op":"close","sid":"t3e"}""");
            Require(close, false, false);
            Console.WriteLine("PASS T3e probe: real host Pipe + hello + kana + commit + close.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL T3e probe: " + ex);
            return 1;
        }
    }

    private static JsonDocument Exchange(NamedPipeClientStream pipe, string request)
    {
        var payload = Encoding.UTF8.GetBytes(request);
        pipe.Write(payload);
        pipe.Flush();
        var buffer = new byte[8192];
        using var stream = new MemoryStream();
        do
        {
            var size = pipe.Read(buffer);
            if (size <= 0) throw new IOException("Pipe closed mid-response.");
            stream.Write(buffer, 0, size);
            if (stream.Length > 64 * 1024) throw new IOException("Reply too large.");
        }
        while (!pipe.IsMessageComplete);
        return JsonDocument.Parse(stream.ToArray());
    }

    private static void Require(JsonDocument message, bool active, bool consumed)
    {
        var root = message.RootElement;
        if (root.GetProperty("version").GetInt32() != MeltiaTipIdentity.ProtocolVersion ||
            root.GetProperty("active").GetBoolean() != active ||
            root.GetProperty("consumed").GetBoolean() != consumed ||
            root.GetProperty("commits").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Unexpected v1 host response.");
    }
}
