// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>確定後の補正、言語学習、変換エンジンへの学習通知（移行段階では CompositionController の状態を共有する）。</summary>
public sealed partial class CompositionController
{
    /// <summary>
    /// 英単語で終わる文字列の最後の語が、よくある打ち間違い (teh、recieve) なら正しい綴りにする (Space で確定するとき)。
    /// </summary>
    private string FixEnglishTypo(string text)
    {
        if (!_options.CorrectTypos()) return text;
        var start = text.Length;
        while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
        if (start == text.Length || _detector.EnglishAutoCorrection(text[start..]) is not { } right) return text;
        Diagnostics.Log.Decision($"英語の打ち間違いを直しました: {Diagnostics.Log.Text(text[start..])}→{Diagnostics.Log.Text(right)}");
        return text[..start] + right;
    }

    /// <summary>
    /// ユーザーが自分で英字 / かなに直して確定した語を覚える (api と打って F10 で英字にした → 次から api は英字)。
    /// 自動の判定と違う方を選んだときだけ。1 語の英字だけを対象にする。
    /// </summary>
    private void LearnLanguage()
    {
        if (_options.Languages is not { } memory) return;
        var raw = _text.Raw;
        if (raw.Length < 2 || !raw.All(char.IsAsciiLetter)) return;
        var automatic = _text.Segments(final: true);
        switch (_text.Mode)
        {
            case DisplayMode.HalfWidthAlphanumeric or DisplayMode.FullWidthAlphanumeric when !automatic.All(s => s.IsEnglish):
                memory.Remember(raw, english: true, explicitChoice: true);
                break;
            case DisplayMode.Hiragana or DisplayMode.Katakana when automatic.Any(s => s.IsEnglish):
                memory.Remember(raw, english: false, explicitChoice: true);
                break;
            case DisplayMode.Auto when _text.LevelOverride is not null && automatic.All(s => s.IsEnglish):
                // 判定の強さが手動で、Tab で提案どおり英字にした。
                memory.Remember(raw, english: true, explicitChoice: true);
                break;
        }
    }


    /// <summary>
    /// 直前に確定した語。英語とも日本語とも読める語 (i, sushi) を文脈が分からないまま確定したとき、
    /// 次の語で文脈がはっきりしたら確定し直す (I want: 「胃」→「I」)。キャレットが動いたら (ほかのキー・クリック) 無効。
    /// </summary>
    private sealed record CommitRecord(string Text, string Raw, bool English, bool SpaceIntended);

    /// <summary>続けて確定した、英語とも日本語とも読める語 (古い順)。make sure you → have で全部英語に直す。</summary>
    private readonly List<CommitRecord> _correctable = [];
    private const int MaxCorrectable = 4;
    private bool _spaceStartedConversion;

    /// <summary>今の変換を Tab で始めたか (変換中の Tab で候補を送るため。issue #219)。</summary>
    private bool _tabStartedConversion;

    /// <summary>キャレットが動いたかもしれないとき (Meltype を通らなかったキー・クリック)。直前の語は確定し直さない。</summary>
    public void ForgetLastCommit() => _correctable.Clear();

    /// <summary>
    /// 今確定しようとしている語 (raw) で文脈がはっきりしたら、直前に確定した英語とも日本語とも読める語を確定し直す。
    /// 例: 「i」を Space で「胃」にした後に want と打つ → 「I want」、「sushi 」の後に「がすき」 → 「すしがすき」。
    /// </summary>
    private string? CorrectPreviousCommit(string raw, bool english)
    {
        if (_correctable.Count == 0 || !_options.AutoCorrect() || !_host.CanDeleteBackward) return null;
        var previous = _correctable[^1];
        List<CommitRecord> targets = [];
        string? replacement = null;
        if (!previous.English && english && _detector.IsDefinitelyEnglish(raw))
        {
            // 日本語で確定した語を英語に: 代名詞の i は I にし、Space で変換していたなら空白も入れる。
            // その前にも Space で区切って日本語にした語が続いていれば、まとめて英文に直す (make sure you have)。
            var start = _correctable.Count - 1;
            while (start > 0 && !_correctable[start - 1].English && _correctable[start - 1].SpaceIntended) start--;
            targets = _correctable.Skip(start).ToList();
            // 助詞と同じ形の短い語 (to, no) 1 語だけを、大文字で始まる語 (固有名詞) で直すことはしない (Google と Apple は日本語でもよく書く)。
            // 小文字の英単語で英文と分かったとき (let me know、do it) は直す。
            if (targets.Count == 1 && targets[0].Raw.Length <= 2 && targets[0].Raw != "i" && char.IsAsciiLetterUpper(raw.FirstOrDefault(char.IsAsciiLetter))) return null;
            replacement = string.Concat(targets.Select(t => (t.Raw == "i" ? "I" : t.Raw) + (t.SpaceIntended ? " " : "")));
        }
        else if (previous.English && !english && !_detector.IsAmbiguousWord(raw) && raw.Any(char.IsAsciiLetter))
        {
            // 英語で確定した語を日本語に (Space で空白を入れていたら取る)。
            targets = [previous];
            replacement = _detector.Romaji.ConvertLenient(previous.Raw.ToLowerInvariant(), final: true);
            // ローマ字として読んでもよく使う語の読みにならない語 (issue → いっすえ、api → あぴ) は英語のまま (issue #121, #124)。
            // sushi → すし のように、日本語の語として読めるときだけ直す。
            if (_options.RomajiTypos is { } lexicon && !lexicon.IsWord(replacement)) return null;
        }
        // ユーザーが英字 / かなに直して覚えた語 (F10 で英字にした api) は、覚えたとおりなら書き換えない (issue #124)。
        if (targets.Any(t => _options.Languages?.Get(t.Raw.ToLowerInvariant()) == t.English)) return null;
        var original = string.Concat(targets.Select(t => t.Text));
        if (replacement is null || replacement == original) return null;
        // 直前に確定した語が、今のキャレットの位置に無い (別の場所をクリックした等) ときは書き換えない。
        // 消すつもりの文字がそこに無いので、消すとキャレットの前の別の文字が消えてしまう。
        // 入力欄の文字をその場で読めるホストだけ確かめる (Windows は別のスレッドで後から読むので、古い値で誤って止めない)。
        if (_host.SurroundingTextIsCurrent && _caretBefore is { } caretBefore && !caretBefore.EndsWith(original, StringComparison.Ordinal))
        {
            Diagnostics.Log.Decision($"キャレットの位置が変わっているので確定し直しません: {Diagnostics.Log.Text(original)}");
            _correctable.Clear();
            return null;
        }

        Diagnostics.Log.Decision($"前後の文脈に合わせて確定し直しました: {Diagnostics.Log.Text(original)}→{Diagnostics.Log.Text(replacement)}");
        _host.ReplaceBackward(original.Length, replacement);
        _lastCommitText = replacement;
        _correctable.Clear();
        return replacement;
    }

    /// <summary>選び直した文節を学習する (次に同じ読みを変換したとき最初の候補にする)。</summary>
    private void Learn()
    {
        // 変換の候補から打ったままの英字 (api) を選んで確定したら、その語は次から英字にする (F10 と同じ)。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed && c.Raw is { } raw && c.Text == raw))
        {
            _options.Languages?.Remember(clause.Raw!, english: true);
        }
        // 英語と判定した語で日本語の候補 (go → 語) を選んだら、その語は次から日本語にする (F6 と同じ)。
        foreach (var clause in _clauses.Where(c => c.IsEnglish && IsJapaneseText(c.Text) && c.Reading.All(char.IsAsciiLetter)))
        {
            _options.Languages?.Remember(clause.Reading, english: false);
        }
        // 英訳を選んだら、英訳の記録に (普通の変換の学習より弱く効く)。
        foreach (var clause in _clauses.Where(c => c.Translations.Contains(c.Text)))
        {
            _options.TranslationHistory?.Remember(clause.Reading, clause.Text);
        }
        LearnConversion();
        if (_options.History is not { } history) return;
        // 1 文字の読み (き → 記) を覚えると、関係ない変換 (き + ごうとう) まで巻き込むので 2 文字以上だけ。
        foreach (var clause in _clauses.Where(c => !c.IsEnglish && c.Changed && c.Reading.Length >= 2))
        {
            // かな・カタカナのまま確定したのは、その場限りのことが多いので覚えない。
            // 打ったままの英字を選んだのは、上で英語として覚えた。
            if (clause.Text == clause.Reading || clause.Text == CompositionText.ToKatakana(clause.Reading) || clause.Text == clause.Raw) continue;
            // 英訳は上で英訳の記録に入れた (ここで覚えると次から 1 番目に出てしまう)。
            if (clause.Translations.Contains(clause.Text)) continue;
            if (IsDateTimeChoice(clause)) continue;
            history.Remember(clause.Reading, clause.Text);
        }
    }

    /// <summary>
    /// 変換エンジン (Mozc) にも、確定した文節を覚えさせる。英語の文節・打ったままの英字で区切った、日本語の文節のまとまりごとに。
    /// 変換エンジンとのやり取りで入力を待たせないよう、裏で行う。
    /// </summary>
    private void LearnConversion()
    {
        if (_converter is not ILearningConverter learner) return;
        var context = ConversionContext();
        var run = new List<ConversionClause>();
        void Flush()
        {
            if (run.Count == 0) return;
            var clauses = run.ToList();
            run.Clear();
            ThreadPool.QueueUserWorkItem(_ => learner.Learn(context, clauses));
        }
        foreach (var clause in _clauses)
        {
            if (clause.IsEnglish || clause.Text == clause.Raw || clause.Translations.Contains(clause.Text) || clause.Reading.Any(char.IsAsciiLetterOrDigit) || IsDateTimeChoice(clause))
            {
                Flush();
                continue;
            }
            run.Add(new ConversionClause(clause.Reading, clause.Text));
        }
        Flush();
    }
}
