// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
namespace Meltia.Core.Boundary;

/// <summary>
/// H10 editing-consistency reference session. Full recomputation is deliberate:
/// it is an oracle for future incremental-cache implementations, NOT an O(1)
/// incremental decoder and never writes confirmed host text.
/// </summary>
public sealed class H10PrototypeSession
{
    private readonly H10BoundaryPrototype _engine;
    public string Raw { get; private set; } = string.Empty;
    public int Caret { get; private set; }

    public H10PrototypeSession(H10BoundaryPrototype? engine = null) =>
        _engine = engine ?? new H10BoundaryPrototype();

    public H10BoundaryPrototype.Analysis Analyze(BoundaryEvidence? evidence = null) =>
        _engine.Analyze(Raw, evidence);

    public void MoveCaret(int index)
    {
        ValidateBoundary(index);
        Caret = index;
    }
    public void Insert(string text) => Replace(Caret, 0, text);

    public void Replace(int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        if (length < 0 || start < 0 || start > Raw.Length - length)
            throw new ArgumentOutOfRangeException(nameof(length));
        ValidateBoundary(start);
        ValidateBoundary(start + length);
        if (Raw.Length - length + replacement.Length > H10BoundaryPrototype.MaxInputLength)
            throw new ArgumentOutOfRangeException(nameof(replacement));
        Raw = string.Concat(Raw.AsSpan(0, start), replacement.AsSpan(),
            Raw.AsSpan(start + length));
        Caret = start + replacement.Length;
    }
    public void Backspace()
    {
        if (Caret == 0) return;
        int width = Caret > 1 && char.IsHighSurrogate(Raw[Caret - 2]) &&
            char.IsLowSurrogate(Raw[Caret - 1]) ? 2 : 1;
        Replace(Caret - width, width, "");
    }
    public void DeleteForward()
    {
        if (Caret >= Raw.Length) return;
        int width = Caret + 1 < Raw.Length && char.IsHighSurrogate(Raw[Caret]) &&
            char.IsLowSurrogate(Raw[Caret + 1]) ? 2 : 1;
        Replace(Caret, width, "");
    }
    private void ValidateBoundary(int index)
    {
        if (index < 0 || index > Raw.Length ||
            index > 0 && index < Raw.Length &&
            char.IsHighSurrogate(Raw[index - 1]) && char.IsLowSurrogate(Raw[index]))
            throw new ArgumentOutOfRangeException(nameof(index), "Invalid UTF-16 caret");
    }
}
