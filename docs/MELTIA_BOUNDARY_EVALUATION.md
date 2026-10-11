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
- `LanguageRegionExactCases` / `TopKLanguageRegionExactCases`: 連続する英語単語spanを1つの英語**言語領域**に結合してから、Top1 / TopK完全一致を測る。英語単語同士の境界が誤りでも言語切替が正しければこちらは正答。S06のような隣接英単語を、単語境界と日英切替境界の両方で同じ得点として数えないための別指標。
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


## CI実測結果：gold v1.1（2026-10-12 JST）

**実行済み（観測値）**：GitHub Actionsの[評価実行](https://github.com/Fugu0141/Meltia/actions/runs/38180568703)、
対象Meltiaコミット `3a323c461a25a358907f118db797e7c8f976567c`。
[全47件のJSON artifact](https://github.com/Fugu0141/Meltia/actions/runs/38180568703/artifacts/11716709437)（GitHubアクセス権が必要）。
実行は.NET10/Ubuntu、Meltypeはオフライン`CompositionText`経由。Goldはv1.1。

- 合成ケース **47**、ユニークraw **43**、Gold英字span **47**、3方式とも例外 **0**。

| 条件 | Top1完全一致 | TopK完全一致 | 枝刈り後Gold候補包含率 | Switch micro F1 |
| --- | ---: | ---: | ---: | ---: |
| Meltype-offline | **26/47 (55.3%)** | 26/47 (55.3%) | N/A（候補一覧なし） | **0.5979** |
| Meltia-independent（語彙入力なし） | 12/47 (25.5%) | 12/47 (25.5%) | **6/47 (12.8%)** | 0.3438 |
| Meltia-shadow-advised（Meltype参考） | 12/47 (25.5%) | **32/47 (68.1%)** | **31/47 (66.0%)** | 0.3438 |

**学べたこと：** シャドー条件では正解経路がTopKに残った47件中32件のうち、
最優先経路として選べたのは12件。**候補の選択順位に改善余地がある**。
ただし同時にGold候補の欠落が16件分あり、順位変更だけで全件正解にはできない。
TopKはMeltypeアドバイスを使用した結果なので、独立したMeltiaの候補生成力と混同しない。

注目するカテゴリ別Top1（Meltype / Meltia単独 / Meltiaシャドー）：
- 英語文頭 S01: **3/3 / 0/3 / 0/3**（シャドーTopK 3/3）。
- 英語文中 S02: **1/4 / 0/4 / 0/4**（シャドーTopK 2/4）。
- 複数英語島 S05: **0/3 / 1/3 / 1/3**。ここでは限定的にMeltiaの方が当たった例がある。
- 大文字 S10: **2/3 / 2/3 / 2/3**（シャドーTopK 3/3）。
- 日本語のみ S11: **4/5 / 5/5 / 5/5**。Meltypeは1件で英語を誤選択、Meltiaは0件。
- ローマ字別表記 S07: **0/3 / 0/3 / 0/3**（シャドーTopK 2/3）。

具体例（全て合成gold、Goldは原rawの半開区間）：

- `shigotodecustomerga`: Gold `[9,17)`。Meltypeは誤って`[3,7)`も英語とした（`goto`部分）。MeltiaはGoldをTop1に選べない。
- `kinouhastrategywakaru`: Gold `[7,15)`。MeltypeもMeltiaもTop1では英語区間なし。
- `kinouhastrategynorequirementtoiu`: Gold `[7,15);[17,28)`。MeltypeのTop1は英語区間なし。
- `kyouhaKingGnudeSPYAIRde`: Gold `[6,13);[15,21)`。Meltypeの今回のオフライン設定では全体`[0,23)`を英語と誤判定した。

**採用判断：まだ本番IMEへ反映しない。** この結果だけでMeltypeを上回ったとは言えない。
見つかった差は、候補生成の根拠の不足と、候補を持っていても日本語経路を選んでしまう
順位問題の両方。改善案は新しい固定holdoutを準備してから評価し、同じ47件への後付け最適化を主張しない。

研究元の参照SHA：
- H9研究ブランチ: `f265fe19c3cb52b59770731161201e58479fb685`
- 評価標準ブランチ: `f04d307fdd9e982788ef94e2a208c3a52f0cd339`

**注意：** これはH8/H9のPython方式を同じ47件で再実行した試験ではないため、
**C#実装のH9に対する精度差やC#/Pythonパリティは未測定**。
