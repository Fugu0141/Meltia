# Meltia v0.9 — 複数英語区間の文脈的な経路評価（2026-10-12）

> **状態**：研究版・Draft PR [#8](https://github.com/Fugu0141/Meltia/pull/8)。
> 独立Coreの開発版番号 `0.9.0-dev`。**IME・TSF・アプリには未接続**。
> ベースはv0.8 Draft PR #7。v0.7 → v0.8 → v0.9 はそれぞれ別の開発ブランチ。

## 背景と目的

v0.8の既存研究語彙を使った530件の合成試験で、Top1は177→250件、
Top-Kは332→405件へ改善した。一方、**2英語区間・3英語区間の独立Top1は
それぞれ0/54件**。候補を多数生成できても、英語1区間でまとめた誤経路や
英語の一部を日本語ローマ字として残す経路が上位に並ぶことがある。

## v0.9の変更

`MeltiaContextualIslandScoring` は**独立の小さな経路スコアリング関数**。
2つの英語候補が次の条件を同時に満たす場合だけ、
その2区間を含む**完成済みルート**に助詞境界の追加証拠を与える。

- 2区間が重ならず、各区間はASCII英語表記5文字以上。
- 両区間の間が`no`、`to`、`de`、`ga`、`ni`、`wo`、`ha`という
  2文字の日本語ローマ字助詞。
- 両候補の綴り全体は通常の日本語ローマ字では読めない（独立英語の手がかり）。
- 適用する追加加点は4.5、候補それぞれには加点しない。
  **候補単体の順位やOrigin、必須ロックの扱いを変更しない**。

切替は研究用オプション（既定OFF）:

```csharp
var engine = new MeltiaBoundaryEngine(new MeltiaBoundaryOptions(
    ExperimentalUnknownLatinCandidates: true,
    ExperimentalPhoneticLatticeDecoder: true,
    ExperimentalContextualIslandScoring: true));
var result = engine.Analyze("kinouhastrategynorequirementtoiu");
System.Diagnostics.Debug.Assert(!result.AutoCommitAllowed);
```

v0.8の比較モデルは`ExperimentalContextualIslandScoring: false`。
同じ語句はどちらでも同じ候補集合から解析する。

**意味的な限界**：短い日本語助詞が綴りの途中に偶然含まれていても
英語境界があるとは断定できない（例：「tokyo」単独は日英の意図が曖昧）。
未知の日本語全体で誤英語分割がないという保証はない。
助詞以外の接続（空白なし、同一綴り・連続英語、固有名詞など）では
この局所信号だけでは判断できない。

## 品質管理と評価

- v0.7/v0.8/v0.9の同条件モデルを、研究済み47ケースと530ケースに同時実行。
- 3モデル間で**候補集合の完全同一性**を1入力ずつ検査。
- 157件の合成日本語のみ入力のTop1誤英語が既定の比較方式を超えたらCI失敗。
- 比較対象：Gold候補包含率、Top-K、Top1、英語領域、切替F1、失敗の段階、
  複数区間、例外、再現性。過去データに合わせた閾値を後付けしない。
- 47+530は**これまで研究に使った英語語彙を多く共有する合成データ**。
  新しい自然文による人手ラベル付きholdout、実打鍵、メモリ・速度測定が必須。

## 実測結果

CI実測値をこの節へ追記する。数値が不十分でも記録・分析し、
主目的である**独立2〜3英語区間のTop1完全一致**と副作用を区別して評価する。
