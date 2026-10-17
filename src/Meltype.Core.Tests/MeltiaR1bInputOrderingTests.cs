// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltia contributors
// R1b: inherited input capture, release and host side-effect order.
using Meltype.Composition;
using Meltype.Input;

namespace Meltype.Tests;

internal static class MeltiaR1bInputOrderingTests
{
    private static KeyEvent Down(int vk, long tick) => new(vk, 0, false, false, false, tick);
    private static KeyEvent Up(int vk, long tick) => new(vk, 0, false, true, false, tick);
    private static MouseButtonEvent Click => new(0x201, 12, 34, 0);

    [Test]
    public static void R1b_Gate_DoesNotTakeNonStartOrMouseWhenIdle()
    {
        var signals = 0;
        var startChecks = 0;
        var gate = new CaptureGate(() => signals++);
        Assert.True(!gate.OnMouseButton(Click), "idle mouse must pass to OS");
        Assert.True(!gate.OnKey(Down('A', 1), e => { startChecks++; return false; }),
            "non-starting key must pass to OS");
        Assert.True(!gate.TryDequeue(out _), "nothing queued when idle");
        Assert.Equal(1, startChecks);
        Assert.Equal(0, signals);
        Assert.True(!gate.IsCaptured, "gate remains open");
    }

    [Test]
    public static void R1b_Gate_CapturesMixedDownUpMouseInStrictArrivalOrder()
    {
        var signals = 0;
        var gate = new CaptureGate(() => signals++);
        var starts = 0;
        Assert.True(gate.OnKey(Down('K', 1), _ => { starts++; return true; }), "first down captured");
        Assert.True(gate.OnKey(Up('K', 2), _ => throw new Exception("must not reclassify captured key up")),
            "key up captured while composing");
        Assert.True(gate.OnMouseButton(Click), "mouse captured while composing");
        Assert.True(gate.OnKey(Down('Y', 3), _ => throw new Exception("must not reclassify captured key down")),
            "second down captured");
        Assert.Equal(1, starts, "start classifier is only called on first event");
        Assert.Equal(4, signals, "one UI wake per captured input");
        Assert.True(!gate.TryRelease(), "cannot open gate while queue contains events");
        Assert.True(gate.IsCaptured, "release must not lose queued input");

        Assert.True(gate.TryDequeue(out var first), "down queued");
        Assert.Equal((int)'K', first.Key?.Vk ?? -1);
        Assert.True(first.Key?.IsDown == true, "down identity preserved");
        Assert.True(gate.TryDequeue(out var second), "up queued");
        Assert.Equal((int)'K', second.Key?.Vk ?? -1);
        Assert.True(second.Key?.IsUp == true, "up identity preserved");
        Assert.True(gate.TryDequeue(out var third), "mouse queued");
        Assert.Equal(Click, third.Mouse.GetValueOrDefault());
        Assert.True(gate.TryDequeue(out var fourth), "next down queued");
        Assert.Equal((int)'Y', fourth.Key?.Vk ?? -1);
        Assert.True(!gate.TryDequeue(out _), "queue drained");
        Assert.True(gate.TryRelease(), "release after draining succeeds");
        Assert.True(!gate.IsCaptured, "capture flag cleared on release");
    }

    [Test]
    public static void R1b_Gate_AbortPreservesInputOrderAndDoesNotReplayItself()
    {
        var gate = new CaptureGate(() => { });
        gate.OnKey(Down('A', 1), _ => true);
        gate.OnMouseButton(Click);
        gate.OnKey(Up('A', 2), _ => false);
        var rest = gate.Abort();
        Assert.Equal(3, rest.Count);
        Assert.Equal((int)'A', rest[0].Key?.Vk ?? -1);
        Assert.Equal(Click, rest[1].Mouse.GetValueOrDefault());
        Assert.True(rest[2].Key?.IsUp == true, "key-up retained");
        Assert.True(!gate.TryDequeue(out _), "abort drained the queue");
        Assert.True(!gate.IsCaptured, "abort releases capture");
        Assert.Equal(0, gate.Abort().Count, "no duplicated events in second abort");
        Assert.True(!gate.OnMouseButton(Click), "next mouse must pass through");
    }

    [Test]
    public static void R1b_Gate_SignalFailureStillLeavesRecoverableQueuedInput()
    {
        var gate = new CaptureGate(() => throw new InvalidOperationException("UI dispatcher stopped"));
        try
        {
            gate.OnKey(Down('K', 1), _ => true);
            throw new Exception("expected the UI signal exception");
        }
        catch (InvalidOperationException ex)
        {
            Assert.Equal("UI dispatcher stopped", ex.Message);
        }
        var rest = gate.Abort();
        Assert.Equal(1, rest.Count, "input must remain recoverable via Abort");
        Assert.Equal((int)'K', rest[0].Key?.Vk ?? -1);
        Assert.True(!gate.IsCaptured, "gate unlocked after explicit recovery");
    }

    [Test]
    public static void R1b_SuspendInput_ReplaysPendingDownMouseUpInOrderAndOnlyOnce()
    {
        var k = new CompositionTests.Keyboard();
        Assert.True(k.Gate.OnKey(Down('K', 1000), _ => true), "capture down");
        Assert.True(k.Gate.OnMouseButton(Click), "capture click");
        Assert.True(k.Gate.OnKey(Up('K', 1001), _ => false), "capture up");
        k.Controller.SuspendInput();
        Assert.Equal("down:4B|mouse:201|up:4B", string.Join("|", k.Host.Events),
            "SuspendInput must replay unprocessed inputs in exact FIFO order");
        Assert.True(!k.Gate.IsCaptured, "suspension releases gate");
        k.Controller.SuspendInput();
        Assert.Equal(3, k.Host.Events.Count, "second suspension cannot duplicate replay");
    }

    [Test]
    public static void R1b_ReplaceBackward_DefaultHostMethodDeletesBeforeCommit()
    {
        var host = new CompositionTests.FakeHost();
        host.CommitText("abc");
        ((ICompositionHost)host).ReplaceBackward(2, "YZ");
        Assert.Equal("aYZ", host.Document, "replacement produces expected app text");
        Assert.Equal("text:abc|bs:2|text:YZ", string.Join("|", host.Events),
            "replacement uses atomic-order contract: delete then commit");
    }

    [Test]
    public static void R1b_Keyboard_ControlShortcut_CommitsBeforeReplayingAndPairsUp()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        k.Key(VirtualKeys.LShift);
        k.Press('Z');
        k.Key(VirtualKeys.LShift, up: true);
        k.Key(VirtualKeys.LControl, up: true);
        var events = k.Host.Events;
        Assert.Equal(1, events.Count(e => e == "text:かな"), "pending reading committed once");
        var committed = events.IndexOf("text:かな");
        var ctrlDown = events.IndexOf("down:A2");
        var shiftDown = events.IndexOf("down:A0");
        var keyDown = events.IndexOf("down:5A");
        Assert.True(committed >= 0 && ctrlDown > committed && shiftDown > ctrlDown
                    && keyDown > shiftDown, "commit => ctrl => shift => key: " + string.Join("|", events));
        Assert.Equal(1, events.Count(e => e == "down:5A"),
            "shortcut down must be delivered exactly once");
        var keyUp = events.FindIndex(e => e == "up:5A" || e == "passed-up:5A");
        Assert.True(keyUp > keyDown,
            "Z-up must follow its down: " + string.Join("|", events));
        Assert.Equal(1, events.Count(e => e == "up:5A" || e == "passed-up:5A"),
            "shortcut up must be delivered exactly once, whether replayed or passed");
        Assert.Equal(1, events.Count(e => e == "up:A0" || e == "passed-up:A0"),
            "Shift release must reach OS exactly once");
        Assert.Equal(1, events.Count(e => e == "up:A2" || e == "passed-up:A2"),
            "Control release must reach OS exactly once");
        Assert.True(!k.Gate.IsCaptured, "gate reopens after shortcut");
    }

    [Test]
    public static void R1b_Keyboard_MouseCommitsBeforeCtrlAndClick()
    {
        var k = new CompositionTests.Keyboard();
        k.Type("kana");
        k.Key(VirtualKeys.LControl);
        Assert.True(k.Gate.OnMouseButton(Click), "captured click");
        k.Controller.Pump();
        var events = k.Host.Events;
        Assert.Equal(1, events.Count(e => e == "text:かな"), "exactly one commit");
        Assert.True(events.IndexOf("text:かな") < events.IndexOf("down:A2") &&
                    events.IndexOf("down:A2") < events.IndexOf("mouse:201"),
            "pending composition committed before Ctrl+click: " + string.Join("|", events));
        Assert.Equal(1, events.Count(e => e == "mouse:201"), "click replayed once");
        k.Key(VirtualKeys.LControl, up: true);
        Assert.Equal(1, events.Count(e => e == "up:A2" || e == "passed-up:A2"),
            "Ctrl-up delivered once");
    }
}
