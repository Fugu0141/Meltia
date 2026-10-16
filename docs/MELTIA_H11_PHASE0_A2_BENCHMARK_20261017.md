# H11 Phase 0-A2：性能・編集操作ベンチマーク要件と基本設計（2026-10-17）

> 対象は **研究用計測器のみ**。H11/H10/v1.0アルゴリズム、辞書、Windows Preview、TSF、配布フローは変更しない。[Phase 0計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) に基づく。Phase 0-A1 の103件厳密固定は [PR #36](https://github.com/Fugu0141/Meltia/pull/36) で対応済み。

## 1. 要件
- **比較の公平性**：旧v1.0（研究オプション7件をON）、H10、H11の3エンジンへ、全く同じraw・同じBoundaryEvidenceを渡す。同じプロセスと同じ測定条件で計測し、JITウォームアップ後にReleaseビルドで実施。
- **入力分布**：UTF-16の32/128/256/512/1024単位（公開定数MaxInputLength以下）ごとに、ローマ字日本語・英語・日英混在・URL・記号/数字・絵文字・明示英語ロック・明示日本語ロックを決定的に生成する。
- **編集列**：カーソル移動、貼付け、Backspace、DeleteForwardの4操作を含み、同じ編集後rawを各エンジンへ渡して全文再解析コストを測る。**カーソル移動そのもの/編集操作そのものの遅延やWindows打鍵E2Eを測ったとは扱わない**。
- **再現性情報**：テスト生成方式・シードの有無・生入力SHA-256・入力集合SHA-256、CoreソースSHA-256・埋込辞書SHA-256、commit SHA、.NET SDK/runtime、CPU識別情報/論理CPU数、OS/アーキテクチャ、GC/alloc、反復数、Stopwatch精度をJSONへ保存。
- **数値**：1モデル×1入力のウォームアップ後、各反復の経過msと `GC.GetAllocatedBytesForCurrentThread` 差分、p50/p95/p99、GC collection数を保存。タイムスタンプなど非決定的値を不変スナップショットへ含めない。
- **不変条件**：1024を超えない、有効なUTF-16境界だけを扱う、raw非改変、全3エンジンが候補を返す、自動確定できない。異常時は失敗として扱う。
- **分類**：合成無ラベル入力の性能値・経路整合性を、自然文holdoutでの正解率やユーザー実機E2Eへ決して転用しない。

## 2. 実装方針
- `tests/Meltia.H11Benchmark/` の独立した.NET 10 Consoleプロジェクトで **Coreへの参照のみ** 使用し、研究本体への参照逆転・処理変更を避ける。
- CLIは `--self-test`, `--smoke`, `--full` と `--output` をサポート。前者は入力生成/UTF-16/編集条件の契約試験。後者は32/128の短いCI検証、全長の比較計測。実測結果は環境により変動するため、数値閾値でCIを落とさない。
- 実行は `.github/workflows/meltia-h11-benchmark.yml` に追加。PRはセルフテスト＋smokeを実行、手動起動はfull選択可。全長計測のArtifactsはコミットせず、JSONをActionsへアップロード。
- `--full` は長文で計算負荷が高くなり得る。各コマンドを独立ジョブで確認し、メモリ・時間上限を無視して無理に完走させない。
- H11のスコア・候補精度は既存の103件ゲートが守る。ベンチ自体はgoldを生成せず、ラベル付きの正解率へ加えない。

## 3. 合格・監査条件
1. CIでセルフテストとsmokeが成功し、レポートJSONに入力ハッシュ・全モデルのp50/p95/p99/割り当て量と環境情報が存在する。
2. 追加データはすべて公開の合成文字列であり、私的打鍵ログや個人の入力を収集しない。
3. 全長ベンチを実施できた場合、端末・コミット・SDKを併記して結果を追記する。未実施・timeoutの場合は明確にその状態を残す。
4. 103件の厳密固定テストを変更せず、性能計測を精度改善の証拠と呼ばない。

## 4. 実行方法
```powershell
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --self-test
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --smoke --output artifacts/h11-benchmark/smoke.json
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --full --output artifacts/h11-benchmark/full.json
```

## 5. 既知の測定限界
- JIT、OSスケジューラ、GCやCPU周波数に影響され、CIの実測値はキーボード入力のサービスレベル保証ではない。
- 割り当て計測はスレッドローカルで、呼出し中に別スレッドへ逃げる処理の全体量を表さない。
- 文字列生成の合成分布は自然文の代表性を保証しない。独立自然文holdoutの規約は **Phase 0-A3** として別PRで整備する。
