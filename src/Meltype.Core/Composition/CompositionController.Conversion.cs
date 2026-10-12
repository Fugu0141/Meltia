// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro

using System.Text;
using Meltype.Config;
using Meltype.Detection;
using Meltype.Input;

namespace Meltype.Composition;

/// <summary>文節変換、候補の生成・操作、ライブ変換（移行段階では CompositionController の状態を共有する）。</summary>
public sealed partial class CompositionController
{
    /// <summary>
    /// 英語区間はそのまま、日本語区間は文脈を付けて変換エンジンで文節に区切って変換する。
    /// 英語区間も、ローマ字として読めれば日本語の候補 (go → 語 ご ゴ) を後ろに足す。preferJapanese (Shift+Space) なら前に出す。
    /// </summary>
    private void StartConversion(bool preferJapanese = false)
    {
        _tabStartedConversion = false;
        var clauses = new List<Clause>();
        var segments = _text.ConversionSegments();
        for (var s = 0; s < segments.Count; s++)
        {
            var segment = segments[s];
            if (segment.IsEnglish)
            {
                if (segment.IsProtected && !preferJapanese)
                {
                    clauses.Add(new Clause(segment.Raw, true, [segment.Raw]));
                    continue;
                }
                var english = EnglishCandidates(segment.Raw);
                var romaji = RomajiCandidates(segment.Raw);
                clauses.Add(new Clause(segment.Raw, true, preferJapanese && romaji.Count > 0
                    ? Distinct([.. romaji, .. english])
                    : Distinct([.. english, .. romaji])) { Kana = _text.KanaForSegment(s) });
                continue;
            }
            if (segment.Kana.Length == 0) continue;
            var japanese = ConvertJapanese(segment.Kana);
            // 英単語 + する (push|した、deploy|しよう): 変換エンジンは英語の後ろの「した」だけを見て 下・死体・使用 にしてしまう。かなのまま
            if (s > 0 && segments[s - 1].IsEnglish && japanese.Count > 0 && IsSuruAfterEnglish(japanese[0])) Prefer(japanese[0], japanese[0].Reading);
            // 候補の最後に、打ったままの英字 (あぴ → api) と全角の英字も出す (Space を連打して英字に戻せる)。
            var offset = 0;
            foreach (var clause in japanese)
            {
                clause.Raw = _text.RawForReading(s, offset, clause.Reading.Length);
                offset += clause.Reading.Length;
                AddOldKana(clause);
                AddTranslations(clause);
                AddRawCandidates(clause);
                MoveEmojiLast(clause);
            }
            clauses.AddRange(japanese);
        }
        if (clauses.Count == 0) return;
        _clauses = clauses;
        _selectedClause = 0;
        _converting = true;
    }

    /// <summary>
    /// 英訳の候補 (複雑な → complex, complicated …) を、日本語の候補の後ろに足す。
    /// 選んだことのある英訳は前に出す: 1 回なら英訳の中の先頭、2 回以上なら 2 番目 (変換エンジンの 1 番目の候補は動かさない)。
    /// </summary>
    private void AddTranslations(Clause clause)
    {
        if (clause.IsEnglish || _options.Translations is not { } dictionary || !_options.TranslationCandidates()) return;
        var words = dictionary.Lookup(clause.Text, clause.Reading).Where(w => !clause.Candidates.Contains(w)).ToList();
        if (words.Count == 0) return;
        var learned = _options.TranslationHistory?.Get(clause.Reading) ?? [];
        // 選んだ回数の多い順に前へ
        words = learned.Select(l => l.Word).Where(words.Contains).Concat(words.Where(w => !learned.Any(l => l.Word == w))).ToList();
        foreach (var word in words) clause.Translations.Add(word);
        var often = learned.FirstOrDefault(l => l.Count >= 2 && words.Contains(l.Word)).Word;
        if (often is not null)
        {
            clause.Candidates.Insert(Math.Min(1, clause.Candidates.Count), often);
            words.Remove(often);
        }
        clause.Candidates.AddRange(words);
    }

    private static void AddRawCandidates(Clause clause)
    {
        // 全角にした記号 (＃ （ ％) は、半角に戻した候補も出す (Space を続けて押すと半角の # ( %)。
        if (clause.Candidates.Count > 0 && CompositionText.SymbolsToHalfWidth(clause.Candidates[0]) is var half && half != clause.Candidates[0] &&
            !clause.Candidates.Contains(half))
        {
            clause.Candidates.Add(half);
        }
        if (clause.Raw is not { } raw || !raw.Any(c => c is >= '!' and <= '~')) return;
        foreach (var candidate in new[] { raw, CompositionText.ToFullWidth(raw) })
        {
            if (!clause.Candidates.Contains(candidate)) clause.Candidates.Add(candidate);
        }
    }

    /// <summary>
    /// wi / we で打った うぃ / うぇ は、ゐ / ゑ (ヰ / ヱ) にした候補も出す (うぃすきー → ゐすきー、ヰスキー)。
    /// 候補の中の うぃ ウィ うぇ ウェ を置き換えた形を、打ったままの英字の候補より前に足す。
    /// </summary>
    private static void AddOldKana(Clause clause)
    {
        if (clause.IsEnglish || clause.Raw is not { } raw) return;
        var lower = raw.ToLowerInvariant();
        if (!lower.Contains("wi") && !lower.Contains("we")) return;
        if (!clause.Reading.Contains("うぃ") && !clause.Reading.Contains("うぇ")) return;
        static string Old(string text) => text.Replace("うぃ", "ゐ").Replace("うぇ", "ゑ").Replace("ウィ", "ヰ").Replace("ウェ", "ヱ");
        var added = new List<string>();
        foreach (var candidate in clause.Candidates.Concat([clause.Reading, CompositionText.ToKatakana(clause.Reading)]))
        {
            var old = Old(candidate);
            if (old != candidate && !clause.Candidates.Contains(old) && !added.Contains(old)) added.Add(old);
        }
        clause.Candidates.AddRange(added);
    }

    /// <summary>
    /// ユーザー辞書の読みが含まれていれば、その部分は登録した単語の文節にし、残りだけを変換エンジンで変換する
    /// (きごうとうふくめ → 記号等|含め)。含まれていなければ普通に変換する。
    /// </summary>
    private List<Clause> ConvertJapanese(string kana)
    {
        // ローマ字として読めずに残った英字 (こほぃc の c) と半角の記号 (... @) は、変換エンジンに渡すと別の記号 (© ．．． ＠) にされるので、
        // そのままの文節にする。数字は、記号・英字とつながっていればそのまま (3.0 の 3 を 1 文字だけ渡すと ❸ にされる)、
        // それ以外は変換エンジンに渡す (2026ねん → 2026年)。
        var keep = new bool[kana.Length];
        for (var i = 0; i < kana.Length; i++) keep[i] = kana[i] is >= '!' and <= '~' && !char.IsAsciiDigit(kana[i]);
        for (var i = 0; i < kana.Length; i++)
        {
            if (!char.IsAsciiDigit(kana[i])) continue;
            var end = i;
            while (end < kana.Length && char.IsAsciiDigit(kana[end])) end++;
            var touches = (i > 0 && keep[i - 1]) || (end < kana.Length && keep[end]);
            for (var k = i; k < end; k++) keep[k] = touches;
            i = end - 1;
        }
        if (keep.Any(k => k))
        {
            var result = new List<Clause>();
            var start = 0;
            for (var i = 1; i <= kana.Length; i++)
            {
                if (i < kana.Length && keep[i] == keep[start]) continue;
                var run = kana[start..i];
                if (keep[start]) result.Add(new Clause(run, false, Distinct([run, .. _options.Candidates?.Lookup(run) ?? [], CompositionText.ToFullWidth(run)])));
                else result.AddRange(ConvertJapanese(run));
                start = i;
            }
            return result;
        }
        if (_options.UserDictionary?.Split(kana) is not { } pieces) return ConvertWithEngine(kana);
        var clauses = new List<Clause>();
        var registered = new List<Clause>();
        foreach (var (reading, word) in pieces)
        {
            if (word is null)
            {
                clauses.AddRange(ConvertWithEngine(reading));
                continue;
            }
            var clause = new Clause(reading, false, JapaneseCandidates(reading, word));
            clauses.Add(clause);
            registered.Add(clause);
        }
        // 同梱の語句 (しょせん → 所詮) も、文脈の手がかり (試合 → 初戦) と学習で選び直せるようにする。
        // 変換エンジンの文節も、語句の文節を含めた前後で文脈の手がかりを見直す (甲斐性ない + こうかい → 後悔)。
        foreach (var clause in clauses)
        {
            var surrounding = (_precedingText ?? "") + string.Concat(clauses.Where(c => c != clause).Select(c => c.Text)) + (_followingText ?? "");
            var preferred = _options.ContextRules?.Choose(clause.Reading, surrounding) ?? (registered.Contains(clause) ? _options.History?.Get(clause.Reading) : null);
            if (preferred is not null) Prefer(clause, preferred);
        }
        return clauses;
    }

    /// <summary>
    /// 日本語のかなを変換エンジンで文節に区切って変換する。各文節の最初の候補は次の順で決める:
    ///   1. 文脈の手がかり辞書 (前後に 気温 があれば あつい → 暑い)
    ///   2. ユーザーが前に選び直した変換 (学習)
    ///   3. 変換エンジンの結果 (入力欄のキャレットの前の文字を文脈として渡している)
    /// </summary>
    private List<Clause> ConvertWithEngine(string kana)
    {
        // 文脈が無いまま「に」で始まる読みを変換すると、変換エンジンは「に」を語の頭と読む (になってしまう → 担ってしまう)。
        // 前の文脈が取れないときは、仮の文脈「これ」を付けて助詞として読ませる (これ + になってしまう → になってしまう)。
        // 「は」「で」なども助詞になりうるが、仮の文脈を付けると はしる → は知る のように崩れるので「に」だけ。
        var context = ConversionContext();
        var parts = context is not null
            ? ConvertWithContext(kana, context)
            : _converter.ConvertClauses(kana, kana.Length >= 3 && kana[0] == 'に' ? "これ" : null);
        parts ??= [new ConversionClause(kana, _converter.Convert(kana) ?? kana)];
        // 読み全体が補助辞書・絵文字の辞書の語 (かんがえるかお → 🤔) なら、文節に分けずに 1 つの文節にして候補を出す。
        if (parts.Count > 1 && _options.Candidates?.Contains(kana) == true)
        {
            parts = [new ConversionClause(kana, string.Concat(parts.Select(p => p.Text)))];
        }
        parts = JoinSmallKana(parts);
        var clauses = parts.Select(p => new Clause(p.Reading, false, JapaneseCandidates(p.Reading, NormalizeHalfWidth(p.Text, p.Reading)))).ToList();
        for (var i = 0; i < clauses.Count; i++)
        {
            var others = string.Concat(clauses.Where((_, k) => k != i).Select(c => c.Text));
            var surrounding = (_precedingText ?? "") + others + (_followingText ?? "");
            var preferred = _options.ContextRules?.Choose(clauses[i].Reading, surrounding) ?? _options.History?.Get(clauses[i].Reading);
            // 変換エンジンは、文節が「から」だけだと記号 (～) にしてしまう。記号だけの変換結果は、ひらがなの後ろに回す。
            preferred ??= IsSymbolOnly(clauses[i].Text) && clauses[i].Reading.All(c => c is >= 'ぁ' and <= 'ゖ') ? clauses[i].Reading : null;
            // 英単語に挟まれて助詞だけの文節になると、変換エンジンは漢字にしてしまう (github + に + push → 二)。助詞はかなのまま。
            preferred ??= Particles.Contains(clauses[i].Reading) && clauses[i].Text != clauses[i].Reading ? clauses[i].Reading : null;
            // カタカナの語の後ろの「っ」で始まる文節 (スパイダーマ + っ！) は、カタカナの「ッ」にする (スパイダーマッ！)。
            preferred ??= i > 0 && clauses[i].Text.StartsWith('っ') && clauses[i - 1].Text is [.., var last] && last is >= 'ァ' and <= 'ヺ' or 'ー'
                ? "ッ" + clauses[i].Text[1..] : null;
            // 文頭・記号の後ろの「え、」「え？」(聞き返し) を、変換エンジンは 得 にしてしまう (得、知らん)。かなのまま
            preferred ??= IsInterjection(clauses, i) ? clauses[i].Reading : null;
            // がち (ガチで) を、変換エンジンは 勝ち にしてしまう (勝ちでやばい)。勝ち の読みは かち なので、がち は ガチ にする
            preferred ??= clauses[i].Reading.StartsWith("がち", StringComparison.Ordinal) && clauses[i].Text.StartsWith("勝ち", StringComparison.Ordinal)
                ? "ガチ" + clauses[i].Text["勝ち".Length..] : null;
            if (preferred is not null) Prefer(clauses[i], preferred);
        }
        return clauses;
    }

    /// <summary>する の活用 (した・して・しない・しよう・したい …) で始まる読み。</summary>
    private static readonly string[] SuruForms = ["する", "すれ", "した", "して", "しま", "しな", "しよ", "しと", "しちゃ", "しろ", "され", "させ", "せず"];

    /// <summary>英単語の後ろの、する の活用の文節を、変換エンジンが漢字で始めた (した → 下、したい → 死体、しよう → 使用)。</summary>
    private static bool IsSuruAfterEnglish(Clause clause) =>
        SuruForms.Any(clause.Reading.StartsWith) && clause.Text.Length > 0 && !IsKana(clause.Text[0]) && clause.Text != clause.Reading;

    private static readonly HashSet<char> SentencePunctuation = ['、', '。', '，', '．', ',', '.', '！', '？', '!', '?', '…', '‥', '「', '」', '(', ')', '（', '）', ' ', '　'];

    /// <summary>
    /// 文節が「え」だけ (後ろに記号が付いていてもよい) で、文の頭か記号の後ろにあり、後ろが記号か文の終わりか。
    /// こういう「え」は聞き返し・驚き (え、しらん) で、絵 や 得 ではない。
    /// </summary>
    private bool IsInterjection(List<Clause> clauses, int i)
    {
        var reading = clauses[i].Reading;
        if (reading.Length == 0 || reading[0] != 'え' || !reading.Skip(1).All(SentencePunctuation.Contains) || clauses[i].Text == reading) return false;
        var before = i > 0 ? clauses[i - 1].Text : _precedingText ?? "";
        if (before.Length > 0 && !SentencePunctuation.Contains(before[^1])) return false;
        if (reading.Length > 1) return true;
        return i + 1 == clauses.Count || clauses[i + 1].Text is [var next, ..] && SentencePunctuation.Contains(next);
    }

    /// <summary>
    /// 小書きのかな (ぃ ぇ ゃ …) で始まる文節は前の文節とつなげる。小書きのかなから始まる語は無いので、
    /// 変換エンジンの知らない語を区切り間違えたもの (こうぃ → 光|ぃ)。つなげた読みは、その文節だけで変換し直す
    /// (こうぃ → コウィ。wi で打っていれば こゐ も候補に出る)。
    /// </summary>
    private IReadOnlyList<ConversionClause> JoinSmallKana(IReadOnlyList<ConversionClause> parts)
    {
        if (!parts.Skip(1).Any(p => p.Reading.Length > 0 && SmallKana.Contains(p.Reading[0]))) return parts;
        var joined = new List<ConversionClause>();
        foreach (var part in parts)
        {
            if (joined.Count > 0 && part.Reading.Length > 0 && SmallKana.Contains(part.Reading[0]))
            {
                var reading = joined[^1].Reading + part.Reading;
                // 変換しても漢字の後ろに小書きのかなが残る (光ぃ) なら、カタカナ (コウィ) にする。
                var text = Convert(reading);
                if (Enumerable.Range(1, Math.Max(0, text.Length - 1)).Any(i => SmallKana.Contains(text[i]) && !IsKana(text[i - 1]))) text = CompositionText.ToKatakana(reading);
                joined[^1] = new ConversionClause(reading, text);
            }
            else joined.Add(part);
        }
        return joined;
    }

    private const string SmallKana = "ぁぃぅぇぉゃゅょゎァィゥェォャュョヮ";

    private static bool IsKana(char c) => c is (>= 'ぁ' and <= 'ゖ') or (>= 'ァ' and <= 'ヺ') or 'ー';

    /// <summary>
    /// 文脈付きと文脈なしの両方で変換し、文節の区切りが同じなら文脈付き (漢字の選び方だけが文脈で変わる: この本は + あつい → 厚い)、
    /// 区切りまで変わるなら文脈なしを使う。前に同じ語があると、変換エンジンは文脈に引きずられて区切りを変えてしまうため
    /// (「記号等」含め、 + きごうとう → き|ごうとう → 気強盗)。
    /// </summary>
    private IReadOnlyList<ConversionClause>? ConvertWithContext(string kana, string context)
    {
        var withContext = _converter.ConvertClauses(kana, context);
        var plain = _converter.ConvertClauses(kana);
        if (withContext is null || plain is null) return withContext ?? plain;
        return withContext.Select(c => c.Reading).SequenceEqual(plain.Select(c => c.Reading)) ? withContext : plain;
    }

    /// <summary>変換エンジンに渡す文脈: キャレットの前の確定済みの文字のうち、同じ文の日本語の部分 (最大 10 文字)。</summary>
    private string? ConversionContext()
    {
        var text = _precedingText;
        if (string.IsNullOrEmpty(text) || LanguageOf(text) != false) return null;
        var start = text.LastIndexOfAny(SentenceEnds);
        text = text[(start + 1)..].Trim();
        return text.Length == 0 ? null : text.Length > 10 ? text[^10..] : text;
    }

    private static readonly char[] SentenceEnds = ['。', '．', '！', '？', '\n', '\r'];

    private static readonly HashSet<string> Particles = ["は", "が", "を", "に", "で", "と", "も", "へ", "の", "や", "か", "から", "まで", "より"];

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    private static void Prefer(Clause clause, string text)
    {
        clause.Candidates.Remove(text);
        clause.Candidates.Insert(0, text);
        clause.Index = 0;
    }

    /// <summary>
    /// 文節の候補: 文の中での変換結果 → その文節だけでの変換結果 → 補助辞書の同音異義語 → ひらがな → 全角カタカナ → 半角カタカナ
    /// → 日付・時刻 (いま・きょう) → 絵文字・顔文字 (逆順)。
    /// 絵文字・顔文字は最後に逆順で並べるので、変換してすぐ ↑ を押すと、いちばんよく使う絵文字 (えがお → 😊) になる (issue #133)。
    /// </summary>
    private List<string> JapaneseCandidates(string reading, string? inContext)
    {
        // ユーザー辞書に登録した単語は、文の中での変換結果の次に出す (文節の区切りを変えて読みが一致したときなど)。
        var candidates = Distinct(inContext);
        foreach (var word in _options.UserDictionary?.Lookup(reading) ?? []) if (!candidates.Contains(word)) candidates.Add(word);
        if (Convert(reading) is var standalone && !candidates.Contains(standalone)) candidates.Add(standalone);
        foreach (var extra in _options.Candidates?.LookupWords(reading) ?? [])
        {
            if (!candidates.Contains(extra)) candidates.Add(extra);
        }
        var katakana = CompositionText.ToKatakana(reading);
        foreach (var kana in new[] { reading, katakana, CompositionText.ToHalfWidthKatakana(katakana) })
        {
            if (!candidates.Contains(kana)) candidates.Add(kana);
        }
        // 日付・時刻 (いま・なう・きょう) は、かなの後ろに出す (いつもの候補の並びは変えない)
        foreach (var time in TimeCandidates(reading))
        {
            if (!candidates.Contains(time)) candidates.Add(time);
        }
        foreach (var emoji in EmojiBlock(reading))
        {
            if (!candidates.Contains(emoji)) candidates.Add(emoji);
        }
        return candidates;
    }

    /// <summary>今の時刻を表す読み (issue #208)。</summary>
    private static readonly HashSet<string> NowReadings = ["いま", "なう"];

    private static readonly string[] WeekDays = ["日", "月", "火", "水", "木", "金", "土"];

    /// <summary>
    /// いま・なう の文節に今の日時、きょう の文節に今日の日付を候補として出す (issue #208)。
    /// いま → 17:22 / 17時22分 / 午後5時22分 / 2026年10月9日(金) 17時22分 / 10月9日(金) 17:22 …
    /// きょう → 2026年10月9日 / 2026年10月9日(金) / 10月9日(金) / 2026/10/09 / 2026-10-09 / 金曜日 …
    /// </summary>
    private IEnumerable<string> TimeCandidates(string reading)
    {
        var isNow = NowReadings.Contains(reading);
        if (!isNow && reading != "きょう") return [];
        var now = _options.Now();
        var day = WeekDays[(int)now.DayOfWeek];
        var date = $"{now.Year}年{now.Month}月{now.Day}日";
        var monthDay = $"{now.Month}月{now.Day}日";
        // 区切りの / と : は、Windows の地域の設定に左右されないように数字から組み立てる (DateTime の書式の / : は地域の区切りになる)
        var slashDate = $"{now.Year:D4}/{now.Month:D2}/{now.Day:D2}";
        var clock = $"{now.Hour:D2}:{now.Minute:D2}";
        if (!isNow)
            return [date, $"{date}({day})", monthDay, $"{monthDay}({day})", slashDate, $"{now.Year:D4}-{now.Month:D2}-{now.Day:D2}", $"{day}曜日"];
        var time = $"{now.Hour}時{now.Minute}分";
        return [clock, time, $"{(now.Hour < 12 ? "午前" : "午後")}{now.Hour % 12}時{now.Minute}分",
            $"{date}({day}) {time}", $"{monthDay}({day}) {time}", $"{date}({day}) {clock}", $"{slashDate} {clock}"];
    }

    /// <summary>
    /// 日付・時刻の候補 (いま → 17:22、きょう → 10月9日) を選んだ文節か。学習しない (覚えると、次に打ったときに古い日時が最初に出る)。
    /// </summary>
    private static bool IsDateTimeChoice(Clause clause) =>
        (NowReadings.Contains(clause.Reading) || clause.Reading == "きょう") && (clause.Text.Any(char.IsAsciiDigit) || clause.Text.EndsWith("曜日", StringComparison.Ordinal));

    /// <summary>絵文字・顔文字の候補を、最後に並べる順 (逆順: いちばんよく使うものが最後) で。</summary>
    private IEnumerable<string> EmojiBlock(string reading) =>
        (_options.Candidates?.LookupEmoji(reading) ?? []).Reverse();

    /// <summary>
    /// 絵文字・顔文字の候補を候補の一覧の最後に移す (英訳・打ったままの英字を足した後に呼ぶ)。
    /// 変換エンジンが最初の候補に絵文字を返したときなど、今選んでいる候補は動かさない。
    /// </summary>
    private void MoveEmojiLast(Clause clause)
    {
        var block = EmojiBlock(clause.Reading).Where(e => clause.Candidates.IndexOf(e) > 0).ToList();
        if (block.Count == 0) return;
        var current = clause.Text;
        clause.Candidates.RemoveAll(block.Contains);
        clause.Candidates.AddRange(block);
        clause.Index = Math.Max(0, clause.Candidates.IndexOf(current));
    }

    /// <summary>英語の文節の候補: 打ったまま → 固有名詞の正しい形 (GitHub) → 先頭だけ大文字 → すべて大文字 → 全角。</summary>
    private List<string> EnglishCandidates(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var capitalized = raw.Length > 0 ? char.ToUpperInvariant(raw[0]) + raw[1..] : raw;
        return Distinct(raw, _detector.ProperNouns.Canonical(lower), capitalized, raw.ToUpperInvariant(), CompositionText.ToFullWidth(raw));
    }

    /// <summary>選んでいる候補の意味: 日本語の意味 (ウィクショナリー)、無ければ英訳 (JMdict)。</summary>
    private string? CandidateMeaning(Clause clause) =>
        _options.Meanings?.Lookup(clause.Text, clause.IsEnglish ? null : clause.Reading) ?? _options.Translations?.Meaning(clause.Text);

    /// <summary>
    /// 候補の右に小さく出す注記: 英訳 (complex)、半角 / 全角 (同じ記号・英数字の半角と全角が両方候補にあるとき。# と ＃ は見分けにくい)。
    /// 注記が 1 つも無ければ null。
    /// </summary>
    private static List<string?>? CandidateNotes(Clause clause)
    {
        var notes = clause.Candidates.Select(c =>
        {
            if (clause.Translations.Contains(c)) return "英訳";
            var half = ToHalfWidth(c);
            if (half != c && clause.Candidates.Contains(half)) return "全角";
            var full = CompositionText.ToFullWidth(c);
            if (full != c && clause.Candidates.Contains(full)) return "半角";
            return null;
        }).ToList();
        return notes.Any(n => n is not null) ? notes : null;
    }

    /// <summary>全角の英数字・記号 (！〜～) と全角の空白を半角にする。</summary>
    private static string ToHalfWidth(string text) =>
        new(text.Select(c => c is >= '！' and <= '～' ? (char)(c - 0xFEE0) : c == '　' ? ' ' : c).ToArray());

    /// <summary>英語と判定した語を、ローマ字として読んだときの候補 (go → 語 ご ゴ)。読み切れなければ空。</summary>
    private List<string> RomajiCandidates(string raw)
    {
        if (_detector.Romaji.AnalyzeFragment(raw.ToLowerInvariant()) is not { IsValid: true, Partial: "" } analysis || analysis.Kana.Length == 0) return [];
        return JapaneseCandidates(analysis.Kana, null);
    }

    /// <summary>日本語の候補 (かな・漢字) か。英語の文節で選ばれたら、その語を日本語として覚える。</summary>
    private static bool IsJapaneseText(string text) => text.Any(c => c is >= '぀' and <= 'ヿ' or >= '㐀' and <= '䶿' or >= '一' and <= '鿿');

    private static List<string> Distinct(params string?[] candidates)
    {
        var list = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrEmpty(candidate) && !list.Contains(candidate)) list.Add(candidate);
        }
        return list;
    }

    private void NextCandidate(int step)
    {
        var clause = _clauses[_selectedClause];
        if (!clause.IsEnglish && !clause.Expanded) Expand(clause);
        clause.Index = (clause.Index + step + clause.Candidates.Count) % clause.Candidates.Count;
        clause.Changed = true;
    }

    /// <summary>
    /// 変換中に文節を選んだときに、変換エンジンの候補の一覧 (はし → 橋 端 箸 …) を 2 番目以降に足す。
    /// 打つたびには呼ばない (時間がかかることがあるため。ライブ変換では呼ばない)。今選んでいる候補はそのまま選んだ状態にする。
    /// </summary>
    private void Expand(Clause clause)
    {
        clause.Expanded = true;
        if (_options.MoreCandidates is not { } more) return;
        var current = clause.Text;
        var merged = new List<string> { clause.Candidates[0] };
        foreach (var candidate in more(clause.Reading).Select(c => NormalizeHalfWidth(c, clause.Reading)).Concat(clause.Candidates.Skip(1)))
        {
            if (!merged.Contains(candidate)) merged.Add(candidate);
        }
        clause.Candidates = merged;
        clause.Index = Math.Max(0, merged.IndexOf(current));
    }

    /// <summary>
    /// 変換エンジンは読みの中の英数字を全角にする (2025ねん → ２０２５年、sだけが → ｓだけが) ので、
    /// 半角で打った数字・英字は半角に戻す。読みに半角の英数字が無ければ (全角を選んだ候補など) そのまま。
    /// </summary>
    internal static string NormalizeHalfWidth(string converted, string reading)
    {
        var digits = reading.Any(char.IsAsciiDigit);
        var letters = reading.Any(char.IsAsciiLetter);
        if (!digits && !letters) return converted;
        var chars = converted.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (digits && chars[i] is >= '０' and <= '９') chars[i] = (char)(chars[i] - 0xFEE0);
            else if (letters && chars[i] is >= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ') chars[i] = (char)(chars[i] - 0xFEE0);
        }
        return new string(chars);
    }

    /// <summary>
    /// Shift+→ / Shift+←: 選択中の文節を 1 文字伸ばす / 縮める。はみ出した・空いた分は次の文節とやり取りし、
    /// 変わった文節は読みから変換し直す。
    /// 英語の文節との区切りを動かすときは、英語の文節を打ったローマ字のかなで日本語の文節に読み直してからやり取りする
    /// (みー|thin|ぐ の みー を伸ばす → みーて|ぃん|ぐ)。縮めて空いた分は、後ろが英語の文節なら英語の文節はそのままにして間に新しい文節を作る。
    /// </summary>
    private void Resize(int delta)
    {
        if (_clauses[_selectedClause].IsEnglish && !ReadAsJapanese(_selectedClause)) return;
        var current = _clauses[_selectedClause];
        var next = _selectedClause + 1 < _clauses.Count ? _clauses[_selectedClause + 1] : null;
        if (next is { IsEnglish: true })
        {
            if (delta < 0) next = null;
            else next = ReadAsJapanese(_selectedClause + 1) ? _clauses[_selectedClause + 1] : null;
        }

        if (delta > 0)
        {
            if (next is null) return;
            current.Reading += next.Reading[0];
            next.Reading = next.Reading[1..];
            if (next.Reading.Length == 0) _clauses.Remove(next);
            else Reconvert(next);
        }
        else
        {
            if (current.Reading.Length <= 1) return;
            var moved = current.Reading[^1];
            current.Reading = current.Reading[..^1];
            if (next is null)
            {
                next = new Clause(moved.ToString(), false, []);
                _clauses.Insert(_selectedClause + 1, next);
            }
            else next.Reading = moved + next.Reading;
            Reconvert(next);
        }
        Reconvert(current);
    }

    /// <summary>英語の文節を、打ったローマ字のかなを読みにした日本語の文節に置き換える。かなに読めない英字だけ (xyz) なら置き換えずに false。</summary>
    private bool ReadAsJapanese(int index)
    {
        if (_clauses[index] is not { IsEnglish: true, Kana: { } kana } || !kana.Any(IsKana)) return false;
        var clause = new Clause(kana, false, []);
        Reconvert(clause);
        _clauses[index] = clause;
        return true;
    }

    private void Reconvert(Clause clause)
    {
        clause.Candidates = JapaneseCandidates(clause.Reading, null);
        clause.Index = 0;
        clause.Changed = false;
        if (_options.History?.Get(clause.Reading) is { } learned) Prefer(clause, learned);
    }

    /// <summary>同じかなを何度も変換しないようにキャッシュする。</summary>
    private string Convert(string kana)
    {
        if (_conversionCache.TryGetValue(kana, out var cached)) return cached;
        var converted = NormalizeHalfWidth(_converter.Convert(kana) ?? kana, kana);
        if (_conversionCache.Count > 256) _conversionCache.Clear();
        _conversionCache[kana] = converted;
        return converted;
    }

    /// <summary>
    /// ライブ変換: Space を押したときと同じく、文脈・手がかり辞書・学習を使って文節ごとに変換した結果を見せる。
    /// 打つたびに呼ばれるので、かな・文脈・学習の状態が同じならキャッシュを使う。
    /// </summary>
    private string LiveConvert(string kana)
    {
        if (kana.Length < LiveConversionMinLength) return kana;
        var key = string.Join("\u0001", kana, _precedingText, _followingText, _options.History?.Version);
        if (_liveCache.TryGetValue(key, out var cached)) return cached;
        var converted = string.Concat(ConvertJapanese(kana).Select(c => c.Text));
        if (_liveCache.Count > 256) _liveCache.Clear();
        _liveCache[key] = converted;
        return converted;
    }

    /// <summary>今の表示 (ライブ変換が有効なら日本語区間は漢字に変換済み)。</summary>
    private string CurrentDisplay(bool final) =>
        _text.Display(final, _options.LiveConversion() ? LiveConvert : null);
}
