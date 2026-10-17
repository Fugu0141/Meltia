# H11 Phase 3 / PR-D1a — ローマ字判定のSpan化と割り当て削減（2026-10-17）

> **独立研究Core内の挙動不変な性能改修。** 起点main `c54797242defe9c42f011841b360aa6ea1aff90c`（Phase 2 C1/C2/C3を統合済み）。上位計画：[H11次期開発準備](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 3 / PR-D1。

## 1. 問題と改良仮説

- 従来`MeltiaRomaji.CanRead(string)`は、入力ごとに`ToLowerInvariant()`の文字列、`new bool[n+1]`配列、遷移ごとに`Substring`を生成して`HashSet<string>`照合を行っている。H11は多数の英字区間を探索するため短い入力でもこのコストが累積する。
- H10・従来v0.8 decoder・v0.7未知英語候補にもSubstringを伴うローマ字判定がある。判定時だけのコピーを`ReadOnlySpan<char>`へ置き換えれば同じ境界判定結果で割り当てを減らせる見込み。
- H11の`RomajiFallbackCost`は同じ英字runのDP中、短い`Substring(i,n)`を多数作り、全runのコストを2回算定することがある。こちらも一回だけ算定する。
- **仮説と合否を分ける。** CIでの意味的互換性に加えて、旧実装とSpan版を同じプロセス・同じ短いASCII入力で比較して`GC.GetAllocatedBytesForCurrentThread`を記録。時間の改善は同一環境の反復測定を行うまで断定しない。

## 2. 要件と基本設計

1. **公開`MeltiaRomaji.CanRead(string)`のAPIを残す。** `null`時の旧動作（`NullReferenceException`）、空文字`true`、UTF-16で1024文字を超える場合`false`、ASCII以外や数字を含む場合`false`、大小ASCII区別なし、`n`単独、`nn`、促音重複、4文字以内の既存音節集合、すべて旧コードと同値。
2. `internal static bool CanRead(ReadOnlySpan<char> value)`を追加。`StringComparer.Ordinal`で構成した既存音節集合と**同一の音節文字列**を起動時一度だけ`uint`へエンコードし、最大4文字のASCII小文字列を衝突のない8-bitの並びとして照合。読取中はASCII lowercaseを一文字ずつ実施し、`stackalloc bool[n+1]`で到達状態を扱う（`n <= 1024`を先に確認し、長大入力ではstackallocしない）。
3. `Islands`、H10日本語辺、v0.8 decoder、v0.7未知英語候補の判定目的`Substring`を必要な場所だけ`AsSpan`に置換。候補が選ばれた後の文字列・スコア・辞書/モデル入力の形式は変更しない。
4. H11の`RomajiReadable`にSpan経路を追加し、従来と**同じ順序**の`cye→che`、`cyu→chu`、`cyo→cho`、`ci→shi`代替正規化を維持する。`RomajiFallbackCost`の小区間はSpan渡しにし、同一runで必要なコストを一度だけ算定する。
5. スコア・重み・音節辞書・英語辞書・正規表現、候補選択・タイブレーク・Top-K探索、Windows Preview/TSF、Release、学習/ユーザー入力ログは変更しない。

## 3. テスト・検証契約

- **凍結した旧コードの判定オラクル**をテストに複製し、`CanRead(string)`と`CanRead(ReadOnlySpan<char>)`（入れ子Span）を同じ入力で比較。全ASCII 1/2/3文字の総当たり、seed固定の長い/短い・混合case・無効文字、`n/nn/nya`、`xtu/ltu`、`ci/cye`等を検査。
- 大文字前後の`Islands`切り分けを旧処理の複製と比較。H11の正規化を触るためH11のFrozen 103件のスパン・Score・順序・トレースは完全一致を必須とする。
- H10と従来Coreは既存のCore契約・47+56件診断/Windows Preview CI、全研究オプションの入力品質を回帰検証する。C2/C3で指摘された「H10/旧Core専用全候補fingerprint不足」は既知の留保で、このPRが解消したとは主張しない。
- 既存 `tests/Meltia.H11Benchmark --smoke` と必要に応じて`--full`を用いる。スレッド割当量と経過時間は反復数・環境を併記し、CIマシン間の数値比較で性能改善を断定しない。
- 新テストは入力例をコード内に固定し、機密入力をログに保存しない。`null`を無理にホスト経路へ投入することはしない。

## 4. 受け入れ条件と次段階

- [ ] 新Span/文字列版と旧凍結オラクルの結果全件一致。H11の103件の厳密Fingerprint合格。
- [ ] 全既存CI（Windows Preview無フック、H10、H11 trace、input-matrix、strict-quality）が成功。
- [ ] allocation/benchmarkの条件・測定値・留保をこのMarkdownに追記。
- [ ] レビュー後にのみmainへマージ。

**PR-D1aの非対象：** H11`H10CharacterEvidence.Difference`と大規模な探索の全面Span化、DP状態・ビーム幅の変更。プロファイル結果次第でPR-D1b/D2に分離する。1回の変更で異なる層を同時に差し替えず、失敗時には小PR単位で戻せる構成とする。

### 手元での再現（.NET 10 / Release）

```sh
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --output artifacts/phase3-d1a-47.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --input tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl --output artifacts/phase3-d1a-56.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --smoke --output artifacts/phase3-d1a-smoke.json
```
