# Meltia v1.0-dev — 不確実性を明示する境界判定Core（2026-10-12）

> **研究用Draft PR [#9](https://github.com/Fugu0141/Meltia/pull/9)。** `Meltia.Core` の
> ビルド版は `1.0.0-dev`。**IME／アプリの完成版、正式リリースではない。**
> PR #8（v0.9）をベースとする積み上げPR。main未マージ。

## v1.0の方針

v0.9の合成評価は、53語規模の研究語彙から作成した530件で独立Top1 273/530、
別の英語40語を使う追加290件で167/290。**候補の列挙・経路の完全一致率を
高めた一方、いまだ多数の誤選択がある。** これまで最上位候補だけを返す
APIでは、ユーザーに「不確かな判定」と「十分に区別された判定」の差が見えない。

**v1.0では最上位経路を変えず、曖昧さの報告を別に追加する。**
このマイルストーンは **境界判定の失敗を明示的に取り扱う堅牢性の改善**であり、
v0.9に対するTop1精度向上を宣言するものではない。

`MeltiaBoundaryOptions.ExperimentalUncertaintyReview`（**既定OFF**）を付けた場合、
`BoundaryResult.Review`に以下を返す。

| フィールド | 意味 |
|---|---|
| `StableEnglishSpans` | スコア差0.75以下の列挙済み経路が一致する最優先英語区間、または明示ロック |
| `ContestedEnglishSpans` | 最優先には存在するが、近接スコア経路で採用が一致しない区間 |
| `UnresolvedReadableEnglish` | 英語辞書／固有名詞証拠があり、文字列全体が日本語ローマ字としても読める、単独ASCII連続区間 |
| `TopTwoScoreGap` | 上位2経路のスコア差（確率ではない） |
| `NeedsConfirmation` | 競合区間か、語彙上の意図が未解決な区間がある |
| `Reason` | どの理由で確認が必要かを列挙する診断コード |

**重要な意味の制限**：
- `Stable`は**列挙されたTop-Kの近接候補に限る一致**であり、実際の正しさ、
  校正済み確率、ユーザーが確定してよい根拠ではない。
- 必須ロックやURLは常に保護されるが、候補に乏しい入力は`NeedsConfirmation=false`
  になり得る。その場合も**正しいと保証しない**。
- 英語としても日本語としても読める`tokyo`/`ime`を文字列だけで解決しない。
  単独ASCII区間に一致する英語辞書証拠があれば意図未解決を示す。
  `nihongo`中の`go`のような部分一致だけでは不確実性警告しない。
- 明示英語ロックがあれば、その区間は意図未解決から外し、強制的に保持。
  `AutoCommitAllowed`は常に**false**。自動確定／変換は実装していない。

## 使い方

```csharp
using Meltia.Core.Boundary;
var engine = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true,
    ExperimentalUncertaintyReview: true));
var result = engine.Analyze("tokyo", new BoundaryEvidence(
    EnglishWords: ["tokyo"]));
System.Diagnostics.Debug.Assert(result.Review!.NeedsConfirmation);
System.Diagnostics.Debug.Assert(!result.AutoCommitAllowed);
```

既定では`Review=null`で、v0.9由来の最優先経路も候補もそのまま。
v0.8/0.9の得点の重み、未知英語候補生成、ビーム幅、原文保持の契約は変更しない。

## バージョン比較・検証計画

研究済み47件、同じ54英語語彙を使う530件、語彙を入れ替えた290件の
**合計867件の合成試験**を同一gold・同一辞書条件で比較する。
各4モード（独立、辞書、Meltype予測、両方）で
`v0.9`と`v1.0`の候補区間・Top-K区間・Top1を全件同一にすることをCIで検査。
v1.0特有の診断レポートは`ReviewMetrics`に出力し、確認必要件数、
確認必要なうちのTop1誤り件数、確認不要なうちのTop1誤り件数を別々に計測。

これは**選択的な予測の安全性評価**であり、Top1が改善したと主張しない。
追加の自然文人手ラベル（ユーザー意図、読み誤り、誤打鍵、略語、固有名詞）と
IME実打鍵p95/p99遅延は未検証。今後の境界予測の改善では、
`NeedsConfirmation=false`でも間違うケースを重点的に調べる。

## 実測

CI実行後、総数だけでなく**確認不要と判断した経路の誤り率**を明記する。
研究語彙での合成評価の優劣だけで採用しない。
