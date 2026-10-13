// SPDX-License-Identifier: GPL-3.0-or-later
namespace Meltia.Core.Boundary;

/// <summary>
/// Editing harness for the provisional boundary engine. Does not send keystrokes
/// or commits to an OS text service. Explicit locks are invalidated on edits.
/// </summary>
public sealed class MeltiaSession
{
    private readonly MeltiaBoundaryEngine _engine;
    private readonly List<Utf16Span> _english = [];
    private readonly List<Utf16Span> _japanese = [];
    public string Raw { get; private set; } = "";
    public int CaretUtf16 { get; private set; }

    public MeltiaSession(MeltiaBoundaryEngine? engine = null) =>
        _engine = engine ?? new MeltiaBoundaryEngine();

    private void ValidateCaret(int index)
    {
        if (index < 0 || index > Raw.Length ||
            index > 0 && index < Raw.Length &&
            char.IsHighSurrogate(Raw[index - 1]) && char.IsLowSurrogate(Raw[index]))
            throw new ArgumentException("Caret splits an invalid UTF-16 boundary");
    }

    public void MoveCaret(int utf16)
    {
        ValidateCaret(utf16);
        CaretUtf16 = utf16;
    }

    public void Insert(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return; // No edit: retain existing explicit language locks.
        if (Raw.Length + value.Length > MeltiaBoundaryEngine.MaxInputLength)
            throw new ArgumentException("Input exceeds boundary engine limit");
        Raw = Raw.Insert(CaretUtf16, value);
        CaretUtf16 += value.Length;
        InvalidateLocks();
    }

    public void Backspace()
    {
        if (CaretUtf16 == 0) return;
        int length = CaretUtf16 > 1 && char.IsLowSurrogate(Raw[CaretUtf16 - 1]) &&
            char.IsHighSurrogate(Raw[CaretUtf16 - 2]) ? 2 : 1;
        Raw = Raw.Remove(CaretUtf16 - length, length);
        CaretUtf16 -= length;
        InvalidateLocks();
    }

    public void DeleteForward()
    {
        if (CaretUtf16 >= Raw.Length) return;
        int length = CaretUtf16 + 1 < Raw.Length &&
            char.IsHighSurrogate(Raw[CaretUtf16]) &&
            char.IsLowSurrogate(Raw[CaretUtf16 + 1]) ? 2 : 1;
        Raw = Raw.Remove(CaretUtf16, length);
        InvalidateLocks();
    }

    public void Lock(Utf16Span span, bool english)
    {
        ValidateCaret(span.Start);
        ValidateCaret(span.End);
        if (span.Start >= span.End) throw new ArgumentException("Empty language span");
        if (english) _english.Add(span);
        else _japanese.Add(span);
    }

    public BoundaryResult Analyze(IReadOnlyList<string>? lexicon = null) =>
        _engine.Analyze(Raw, new BoundaryEvidence(
            EnglishWords: lexicon, ExplicitEnglish: _english, ExplicitJapanese: _japanese));

    /// <summary>Only an explicit host operation may clear this research session.</summary>
    public void Clear()
    {
        Raw = "";
        CaretUtf16 = 0;
        InvalidateLocks();
    }

    private void InvalidateLocks()
    {
        _english.Clear();
        _japanese.Clear();
    }
}
