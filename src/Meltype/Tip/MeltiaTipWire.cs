// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using System.Text.Json;

namespace Meltype.Tip;

/// <summary>
/// A per-connection, opt-in Meltia v1 IPC gate. No session method executes
/// until a versioned hello receives active=true from the existing dispatcher.
/// Not used by the inherited Meltype endpoint or by Keyboard Preview.
/// </summary>
internal sealed class MeltiaTipWire
{
    internal static readonly string Inactive = "{\"version\":" + MeltiaTipIdentity.ProtocolVersion + ",\"active\":false,\"consumed\":false,\"commits\":[],\"view\":null}";
    internal bool Handshaken { get; private set; }

    internal void Reset() => Handshaken = false;

    internal string Process(string request, Func<string, string> dispatch)
    {
        try
        {
            using var message = JsonDocument.Parse(request);
            var root = message.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != MeltiaTipIdentity.ProtocolVersion ||
                !root.TryGetProperty("op", out var operation) || operation.ValueKind != JsonValueKind.String)
            {
                Handshaken = false;
                return Inactive;
            }

            var op = operation.GetString();
            if (op == "hello")
            {
                // A new hello explicitly resets the previous connection state.
                Handshaken = false;
                var response = dispatch(request);
                if (!IsActive(response)) return Inactive;
                Handshaken = true;
                return WithVersion(response);
            }

            if (!Handshaken || op is not ("key" or "commit" or "select" or "close") ||
                !root.TryGetProperty("sid", out var sid) || sid.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(sid.GetString()))
                return Inactive;

            return WithVersion(dispatch(request));
        }
        catch (JsonException)
        {
            Reset();
            return Inactive;
        }
        catch
        {
            // The transport maps dispatcher errors to an inactive response.
            // Invalidate this connection before propagating that failure.
            Reset();
            throw;
        }
    }

    private static bool IsActive(string response)
    {
        using var parsed = JsonDocument.Parse(response);
        return parsed.RootElement.ValueKind == JsonValueKind.Object &&
            parsed.RootElement.TryGetProperty("active", out var active) &&
            active.ValueKind == JsonValueKind.True;
    }

    private static string WithVersion(string response)
    {
        // The inherited dispatcher returns a JSON object. Reject other shapes.
        if (response.Length < 2 || response[0] != '{' || response[^1] != '}')
            return Inactive;
        return "{\"version\":" + MeltiaTipIdentity.ProtocolVersion + "," + response[1..];
    }
}
