# Meltia境界判定 — オフライン比較評価 v1（2026-10-12）

## 目的と証拠の区分

この評価は **B層 / C# Coreのオフライン比較**であり、Windows TSF、Mozc、本番アプリの変換品質ではない。

- ゴールドは `tests/Meltia.BoundaryEvaluation/GoldCorpus.cs` の **47件の合成・入力意図付きケース（gold v1.1）**。入力断片を日本語（false）/英字（true）で明示し、UTF-16半開区間をプログラムで自動構築する。
- 自然文を複数人がラベル確定した `human_verified` コーパス **ではない**。独立なholdout、統計的な汎化性能、H9の5,470件モデル評価の再現を主張しない。
- H7のS01〜S13の分類を各2例以上含む。ほかに同じrawでも異なる意図を持つ`tokyo` / `ime` を別ラベルで採録。
- 研究由来の具体例：H9の `kinouhastrategywakaru`、`kinouhastrategynorequirementtoiu`、`kinouhaSTRATEGYtoiu`、`korekaraknowledge.wakaru`、`dokoniakerusonomamakyouno`。元研究レポートの意図spanを、実際の連結断片から算出。
- `myunknownsite.comniarimasu` は英語spanの終了位置を原研究で訂正した **17** に固定（`myunknownsite.com`は17字）。
- 研究pilot corpus `research/pilot-corpus.jsonl` にある `gold_status=unconfirmed` をgoldとみなしていない。

研究資料の所在（Meltiaに全件移植していない）：

- [H9追加評価](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/research/h9-followup-evaluation-20261010/research/2026-10-10-h9-additional-evaluation.md)
- [H8解析](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/research/h9-followup-evaluation-20261010/research/2026-10-10-h8-global-multispan-study.md)
- [研究評価標準v1.1](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/docs/research-evaluation-standard-20261010/docs/research-evaluation-standard.md)
- [pilot corpus（未確認ラベル）](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/experiment/stream-hybrid-v1.3-latin-morphology/research/pilot-corpus.jsonl)

## 比較条件

| 条件 | 実行内容 | 公平性の解釈 |
|---|---|---|
| `Meltype-offline` | 実 `CompositionText.Append(char)` を1文字ずつ実行して `CompositionDetector.Segment` | Windows入力とは異なるオフラインの比較基準 |
| `Meltia-independent` | `MeltiaBoundaryEngine.Analyze(raw)` に外部語彙・Meltype予測・明示英字ロックを**渡さない** | 独立Coreの現在の生成・選択能力。辞書なしという制約を開示 |
| `Meltia-shadow-advised` | 同じMeltypeのセグメントを読み取り専用アダプタに渡したMeltia Core | Meltypeを入力根拠として使用。独立推定の改善とは解釈しない |

どの条件も入力の文字列とラベルを変更しない。実験のための学習、入力意図ロック、gold語彙の注入、TSFへのテキスト確定、ファイルへのユーザー打鍵保存を行わない。

## 保存する指標

- `ExactCases`: 英語span集合が完全一致した **ケース数**（英語のみ・日本語のみも分母に含める）。単語間の隣接spanの分割も区別。
- `TopKExactCases`: 候補経路のいずれかが全gold spanと一致したケース数。MeltypeはTop1のみ。
- `GoldCandidateRecall`: 候補一覧に含まれた英語gold span数 / gold英語span総数。Meltypeには候補一覧がないため **N/A**。Meltiaは枝刈り後の診断候補であり、生候補Recallを主張しない。
- `SwitchF1Micro`: 内部の英語/非英語の文字境界で計測したmicro F1。文頭0と文末lenは境界に含めず、隣接英単語の**同言語**語彙境界は切替に含めない。
- `SwitchF1Macro`: (TP、FP、FN)に少なくとも1つが存在するケースだけのmacro F1。どのケースも切替が無い場合は **N/A**。
- `JapaneseOnlyFalseEnglishCases`: `S11-japanese-only` だけを分母とした、英語を1区間でも誤選択したケース数。
- `Errors`: 例外発生数。失敗例・原文・期待span・予測span・例外型をJSONに保持。例外が1つでもあればCIの評価ジョブは失敗する。

**未測定：** 日本語内部の単語分割、かな変換結果・CER、単語スコアの同等性、C#/Pythonパリティ、候補生成**前**の生候補、p50/p95/p99逐次入力遅延、TSFやMozc、無制限の自然文精度。

## 実行

```bash
dotnet run --project tests/Meltia.BoundaryEvaluation/Meltia.BoundaryEvaluation.csproj -c Release -- --output artifacts/meltia-boundary-evaluation.json
```

CIの `Meltia composition shadow bridge` ワークフローでも同じコマンドを実行し、全ケースを `meltia-boundary-evaluation` artifactとして保存する。独立した gold 精度の合格閾値は **まだ設定しない**（低い結果を後から都合良く除外しないため）。既存の安全契約テストは引き続き必須。

## 次段階の判定方法

1. 現在の47件での **モデル別・13分類別の失敗例とscore** をCIから再現し、候補欠落／TopKにあるが最優先経路ではない／TopK全体にない、に分ける。
2. H8/H9 Python側の**入力集合そのもの**を、適切なブランチから固定出力として抽出する。合成goldの元断片・seed・語種・文脈テンプレート・Python版・基準SHAを残し、同じ入力でC#を実測する。H8/H9の **Pythonモデル数値をC#の測定値に代入しない**。
3. 文字列が同一で複数意図があるテストについては、人の意図を入力情報なしに復元できないことを明記し、gold条件を別々に集計。
4. 基準線確定後、改善版の候補生成/経路選択を一要因ずつ変更し、seed独立性・日本語誤検出・文頭/中/末・2島以上・略語・表記揺れの回帰を監視。
5. その後にWindows TSFとMozcの実入力、編集後安定性、遅延を別レイヤーで検証する。

**この評価ランナーを導入しただけでは境界精度が上がったわけではない。**


## 評価コーパス訂正履歴

- **v1.0（速報・破棄）**：初回CIの数値を確認後、`githubdeissue` の`issue`を日本語だと誤ラベルしたことを発見。速報のスコアは**比較基準から除外**する。入力意図は`github`（英字）+`de`（日本語）+`issue`（英字）。
- **v1.1（2026-10-12）**：上記のGoldだけを訂正して、全文脈・全モデルの精度を再計測する。ケース数やraw数は変わらず、Gold英字span総数は46→47となる。
- 今後もgold訂正が必要な場合は原因と旧数値を記録し、都合が悪いケースを削除せずに再計測する。
