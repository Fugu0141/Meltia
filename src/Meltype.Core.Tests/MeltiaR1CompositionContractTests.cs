// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// R1: characterize inherited Meltype behavior BEFORE refactoring it.
// These tests neither start hooks nor register a TSF TIP.
using System.Text.Json;
using Meltype.Composition;
using Meltype.Config;
using Meltype.Input;

namespace Meltype.Tests;

internal static class MeltiaR1CompositionContractTests
{
    private static MeltypeSession CreateSession() =>
        new(CompositionTests.Detector, new CompositionTests.FakeConverter(),
            new CompositionOptions(), () => new Settings());

    private static SessionResult Type(MeltypeSession session, string text)
    {
        SessionResult? latest = null;
        foreach (var c in text)
        {
            var vk = c switch
            {
                '\n' => VirtualKeys.Return,
                ' ' => VirtualKeys.Space,
                '\b' => VirtualKeys.Back,
                _ => (int)char.ToUpperInvariant(c)
            };
            latest = session.HandleKey(vk,
                c is '\n' or ' ' or '\b' ? null : c,
                shift: false, control: false, alt: false, command: false);
        }
        return latest ?? throw new InvalidOperationException("Expected nonempty input");
    }

    [Test]
    public static void R1_JsonCommitSchema_PreservesUnicodeAndNoExpectWithoutDelete()
    {
        var input = "日本語😀\"\\\n";
        var result = new SessionResult(true, [new TextEdit(0, input)], null);
        using var json = JsonDocument.Parse(result.ToJson());
        var root = json.RootElement;
        Assert.True(root.GetProperty("consumed").GetBoolean(), "consumed flag is serialized");
        Assert.True(root.GetProperty("view").ValueKind == JsonValueKind.Null,
            "empty view is JSON null");
        var edits = root.GetProperty("commits");
        Assert.Equal(1, edits.GetArrayLength(), "one edit");
        var edit = edits[0];
        Assert.Equal(0, edit.GetProperty("deleteBefore").GetInt32(), "append-only edit");
        Assert.Equal(input, edit.GetProperty("text").GetString(), "UTF-8/Unicode and escaping round trip");
        Assert.True(!edit.TryGetProperty("expect", out _),
            "plain append must omit expect, not send a fake validation string");
    }

    [Test]
    public static void R1_JsonCorrectionSchema_RequiresExactExpect()
    {
        var result = new SessionResult(true, [new TextEdit(3, "XYZ", "abc")], null);
        using var json = JsonDocument.Parse(result.ToJson());
        var edit = json.RootElement.GetProperty("commits")[0];
        Assert.Equal(3, edit.GetProperty("deleteBefore").GetInt32());
        Assert.Equal("abc", edit.GetProperty("expect").GetString());
        Assert.Equal("XYZ", edit.GetProperty("text").GetString());
        // The native TIP must independently validate the previous text before deletion.
        // This test checks the *wire representation*, NOT native enforcement.
    }

    [Test]
    public static void R1_PassThrough_DoesNotCommitOrClearActiveComposition()
    {
        var session = CreateSession();
        var before = Type(session, "kyouha");
        Assert.True(before.Consumed, "composition owns the typed characters");
        Assert.True(session.IsComposing, "a live composition exists");

        var bypass = session.PassThrough();
        Assert.True(!bypass.Consumed, "the OS event must be passed once by caller");
        Assert.Equal(0, bypass.Commits.Count, "no extra committed characters");
        Assert.Equal(before.View?.Text, bypass.View?.Text,
            "pass-through must not silently alter the visible text");
        Assert.True(session.IsComposing, "a no-op is not an implicit cancel");
    }

    [Test]
    public static void R1_CommitForPassThrough_FlushesOnlyOnceAndPassesOriginalKey()
    {
        var session = CreateSession();
        Type(session, "kyouha");
        var flushed = session.CommitForPassThrough();
        Assert.True(!flushed.Consumed, "caller forwards the original OS event once");
        Assert.Equal(1, flushed.Commits.Count, "one pending composition commit");
        Assert.Equal(0, flushed.Commits[0].DeleteBefore,
            "ordinary commit must not delete existing app text");
        Assert.Equal("きょうは", flushed.Commits[0].Text);
        Assert.True(flushed.View is null, "composition UI closed after commit");
        Assert.True(!session.IsComposing, "composition cleared");
        var second = session.CommitForPassThrough();
        Assert.Equal(0, second.Commits.Count, "repeated call cannot commit duplicate text");
        Assert.True(!second.Consumed, "original event remains unconsumed");
    }

    [Test]
    public static void R1_CommitPending_FlushIsIdempotent()
    {
        var session = CreateSession();
        Type(session, "kyouha");
        var first = session.CommitPending();
        Assert.True(first.Consumed, "CommitPending owns its host operation");
        Assert.Equal(1, first.Commits.Count);
        Assert.Equal("きょうは", first.Commits[0].Text);
        Assert.True(!session.IsComposing, "composition cleared");
        var second = session.CommitPending();
        Assert.Equal(0, second.Commits.Count, "no duplicate commit on repeated focus callback");
    }

    [Test]
    public static void R1_AbandonAfterFocusLoss_NeverCommitsIntoNewTarget()
    {
        var keyboard = new CompositionTests.Keyboard();
        keyboard.Type("kyouha");
        Assert.True(keyboard.Controller.IsComposing, "initially composing");
        var before = keyboard.Host.Output.Count;
        var abandoned = keyboard.Controller.Abandon("R1 focus changed");
        Assert.True(abandoned, "pending composition discarded");
        Assert.Equal(before, keyboard.Host.Output.Count,
            "focus loss cannot write into another target");
        Assert.True(!keyboard.Controller.IsComposing, "abandon cleared composition");
        Assert.True(keyboard.Host.View is null, "candidate UI hidden");
        Assert.True(!keyboard.Controller.Abandon("already idle"),
            "repeated focus change is not another commit");
    }

    [Test]
    public static void R1_ExplicitBypass_WhenIdle_IsNeverACommit()
    {
        var session = CreateSession();
        var bypass = session.PassThrough();
        Assert.True(!bypass.Consumed, "idle event is not consumed");
        Assert.Equal(0, bypass.Commits.Count);
        Assert.True(bypass.View is null, "no view while idle");
        Assert.True(!session.IsComposing, "composition cleared");
    }
}
