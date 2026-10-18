// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
using Meltype.Tip;
using System.Text.Json;

var passed = 0;
var failed = 0;
const string Active = "{\"active\":true,\"consumed\":false,\"commits\":[],\"view\":null}";
const string Inactive = "{\"active\":false,\"consumed\":false,\"commits\":[],\"view\":null}";

void Check(string name, Action run)
{
    try { run(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
void Expect(bool ok) { if (!ok) throw new InvalidOperationException("assertion failed"); }
bool IsActive(string json)
{
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("active").GetBoolean();
}
bool IsVersioned(string json)
{
    using var doc = JsonDocument.Parse(json);
    return doc.RootElement.GetProperty("version").GetInt32() == MeltiaTipIdentity.ProtocolVersion;
}
Check("unknown version rejects hello before dispatch", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    var result = gate.Process("{\"op\":\"hello\",\"version\":2}", _ => { count++; return Active; });
    Expect(!IsActive(result) && IsVersioned(result) && count == 0 && !gate.Handshaken);
});
Check("missing or noninteger version is inert", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    foreach (var json in new[] {"{\"op\":\"hello\"}", "{\"op\":\"hello\",\"version\":\"1\"}",
        "{\"op\":\"hello\",\"version\":1.5}", "{\"op\":\"hello\",\"version\":null}"})
        Expect(!IsActive(gate.Process(json, _ => { count++; return Active; })));
    Expect(count == 0);
});
Check("key before hello cannot create session", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    var reply = gate.Process("{\"op\":\"key\",\"version\":1,\"sid\":\"s\"}",
        _ => { count++; return Active; });
    Expect(!IsActive(reply) && count == 0);
});
Check("inactive hello does not enable sessions", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    var first = gate.Process("{\"op\":\"hello\",\"version\":1}", _ => { count++; return Inactive; });
    var second = gate.Process("{\"op\":\"key\",\"version\":1,\"sid\":\"s\"}", _ => { count++; return Active; });
    Expect(!IsActive(first) && !IsActive(second) && count == 1 && !gate.Handshaken);
});
Check("hello then five operations preserve response schema", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    Expect(IsActive(gate.Process("{\"op\":\"hello\",\"version\":1}", _ => { count++; return Active; })));
    foreach (var op in new[] {"key","commit","select","close"})
    {
        var json = "{\"op\":\"" + op + "\",\"sid\":\"s\",\"version\":1}";
        var result = gate.Process(json, _ => { count++; return Active; });
        Expect(IsVersioned(result) && IsActive(result));
        using var parsed = JsonDocument.Parse(result);
        Expect(!parsed.RootElement.GetProperty("consumed").GetBoolean());
        Expect(parsed.RootElement.GetProperty("commits").GetArrayLength() == 0);
    }
    Expect(count == 5 && gate.Handshaken);
});
Check("unknown operation and absent sid are inert", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    gate.Process("{\"op\":\"hello\",\"version\":1}", _ => { count++; return Active; });
    Expect(!IsActive(gate.Process("{\"op\":\"deleteEverything\",\"version\":1,\"sid\":\"s\"}", _ => { count++; return Active; })));
    Expect(!IsActive(gate.Process("{\"op\":\"key\",\"version\":1}", _ => { count++; return Active; })));
    Expect(!IsActive(gate.Process("{\"op\":\"key\",\"version\":1,\"sid\":\"\"}", _ => { count++; return Active; })));
    Expect(count == 1);
});
Check("version downgrade revokes prior handshake", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    gate.Process("{\"op\":\"hello\",\"version\":1}", _ => { count++; return Active; });
    var bad = gate.Process("{\"op\":\"key\",\"version\":5,\"sid\":\"s\"}", _ => { count++; return Active; });
    var later = gate.Process("{\"op\":\"key\",\"version\":1,\"sid\":\"s\"}", _ => { count++; return Active; });
    Expect(!IsActive(bad) && !IsActive(later) && count == 1 && !gate.Handshaken);
});
Check("malformed payload revokes prior handshake", () =>
{
    var gate = new MeltiaTipWire(); var count = 0;
    gate.Process("{\"op\":\"hello\",\"version\":1}", _ => { count++; return Active; });
    var bad = gate.Process("not JSON", _ => { count++; return Active; });
    Expect(!IsActive(bad) && !gate.Handshaken && count == 1);
});
Check("separate connections cannot reuse handshake state", () =>
{
    var first = new MeltiaTipWire(); var second = new MeltiaTipWire(); var count = 0;
    first.Process("{\"op\":\"hello\",\"version\":1}", _ => { count++; return Active; });
    Expect(!IsActive(second.Process("{\"op\":\"key\",\"sid\":\"s\",\"version\":1}", _ => { count++; return Active; })));
    Expect(count == 1 && first.Handshaken && !second.Handshaken);
});
Console.WriteLine($"T3b offline tests: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
