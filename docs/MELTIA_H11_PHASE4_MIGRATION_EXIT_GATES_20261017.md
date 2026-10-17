# H11 Phase 4 — 共通Decoderの移行成果・採用判定ゲート（2026-10-17）

> E1/E2はmainにMerge済み。E3→E4→E5はレビュー待ちの積み上げPRであり、**Phase4全体の採用完了や製品版への組み込みを意味しない**。本書は次の変更が無計画な常用切替にならないようにするための引継ぎと、同じ入力条件での性能比較・採用条件を固定する。

## 1. 構造移行の進捗

| PR | 経路 | 共通化した責務 | 旧オラクル | 最終Core検証 | 適用状態 |
| --- | --- | --- | --- | --- | --- |
| [#46](https://github.com/Fugu0141/Meltia/pull/46) E1 | H11前向き（幅16） | forward DP・候補・遷移・beam | H11 Analyze | 238件時点の契約 | main統合、実験専用 |
| [#47](https://github.com/Fugu0141/Meltia/pull/47) E2 | H11 | 候補Generator・Top-K射影 | 旧前向き・射影 | 244件時点の契約 | main統合、実験専用 |
| [#48](https://github.com/Fugu0141/Meltia/pull/48) E3 | H10前向き（幅8） | モデル別候補・遷移・beam・射影 | H10 Analyze | **250 PASS** | PR、レビュー待ち |
| [#49](https://github.com/Fugu0141/Meltia/pull/49) E4 | v0.8/0.9後向き | backward DP・候補展開・区間多様性Retention | v0.8 Decode | **256 PASS** | 依存PR、レビュー待ち |
| [#50](https://github.com/Fugu0141/Meltia/pull/50) E5 | 従来Core後向き | backward DP・単純Top8/DistinctBy保持 | Core BuildRoutes | **262 PASS** | 依存PR、レビュー待ち |

> 数字は**各時点の同一Coreスイート総チェック件数**。262はE3/E4/E5を含む後続コードHEADでの結果であり、独立した262例の言語精度ではない。各行の結果を足し算して精度分母に用いない。

## 2. 確定した探索意味の違い

- **H11**：前向き位置×3言語状態、閾値32件超→16件、言語切替 -1.6、特定の辞書語→日本語遷移に +0.9、最大8本。
- **H10**：前向き位置×3状態、閾値24件超→8件、言語切替 -2.0、独自の不確実判定。H11と共通なのは前向き走査の骨格のみ。
- **v0.8系**：終端→始端、romaji可読性・未知ASCIIコスト・英語境界文脈・候補別スコアを合成。経路を**英語区間数別に代表保持**してから残りを補完。単純Top-Kでは候補の多様性を失う。
- **従来Core**：終端→始端、mandatoryと候補による経路展開、`Score`降順・UTF-16区間キーによるタイブレーク・署名dedup・上位8。v0.8の区間数代表保持は使わない。

前向き/後向きの汎用ループは**別の抽象**にし、モデル固有Retentionを統合したり、H12で独立探索ループを再度コピーしない。

## 3. 通常経路へ切り替える前の採用ゲート（未達）

1. **コードレビューと依存PR取り込み**：#48→#49→#50を独立レビュー。E3のMerge後に#49のbaseをmainへ更新し、E4のMerge後に#50のbaseをmainへ更新。各回で差分・必要なCI・未解決指摘を確認。設計・スコア変更は別PR。
2. **全観測値の互換契約**：同じraw、Evidence、候補/辞書/model SHA、option/明示ロックで、raw保全、候補のspan/origin/score/順序、Top-K全経路、同点処理、確認要フラグ、Trace・例外、Unicode・保護区間が完全一致。H11は既存103件の独立固定fingerprintを継続。
3. **同一runnerの性能評価**：`main`固定SHAと採用候補HEADを一つのActionsジョブ内で **A→B→B→A**、.NET10 Release、同じSDK・CPU・辞書/fixture SHA256で比較。32/128/256/512/1024 UTF-16、英字/romaji/日英混在/URL/絵文字・ロック、編集後スナップショット、事前warmup、p50/p95/p99、割当/GCを記録。系列を混ぜた全体p50だけで採用判定しない。E1/E2のラッパー/列挙器の追加割り当ても測る。テストコードや実験器自体を採用判断に影響させない。
4. **危険経路の入力テスト**：Windows Previewの実キー打鍵（Backspace/Enter/候補/確定/編集/貼付け/日本語と英語の切替/長時間入力/URL/emoji）、異常時の原文保全・オプトアウト・フォールバックを実機で確認。CIの無フックself-testを実打鍵E2Eと混同しない。正式TSFはさらに別レビューが必要。
5. **一般化評価**：辞書内外・EN-OOV・助詞継ぎ目・日本語負例・意図衝突・複数英語区間を層別化。独立holdoutの封印・採点を壊さず、既知103例や合成例への最適化を言語精度の一般化とみなさない。
6. **段階ロールアウト**：通常 `Analyze` のデフォルト切替を性能・互換・Windows安全試験に先行させない。切替は独立PR、既定OFF→制御可能ON→限定配布の順に行い、失敗時に旧経路へ戻る仕組みを仕様化してからレビューする。自動確定は引き続き不可。

**いずれかのゲートが未検証または失敗なら旧経路を維持する。** 高速化だけを理由にビームの順序や候補削除規則を変更しない。性能改善を意図する変更は互換リファクタリングと別PRに切り分ける。

## 4. 実証済みと未実証の区別

- **実証済み**：E3/E4/E5の追加Core契約は、固定103raw、seed付き混在、長文・ロック・例外を含み旧/新の全経路を比較して成功。GitHub Actionsで各HEADのCore・H10/H11・品質・Windows Preview無フックを実行。
- **未実証**：新Decoderを通常H11/H10/Core/V0.8の既定経路に切替えた実キー入力E2E、mainとの同一runnerペアベンチマーク、独立自然文holdout、データの一般化精度、正式TSF/安定版の保証。
- E3/E4/E5の実装は本番の推論先を変更しない。PRがmainへ入っても**通常経路の切替ではない**。

## 5. 引継ぎ・再実行

```bash
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --smoke --output artifacts/phase4-decoder-smoke.json
```

参照： [E3](MELTIA_H11_PHASE4_E3_H10_FORWARD_ADAPTER_20261017.md)、[E4](MELTIA_H11_PHASE4_E4_BACKWARD_DECODER_20261017.md)、[E5](MELTIA_H11_PHASE4_E5_LEGACY_BACKWARD_20261017.md)。
