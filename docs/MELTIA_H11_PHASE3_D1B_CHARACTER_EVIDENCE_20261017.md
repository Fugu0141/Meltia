# H11 Phase 3 / PR-D1b — 文字尤度計算のSpan化と割り当て削減（2026-10-17）

> 起点main: `2ebf6976c457a8e84bc1a0b69aa26fce3f7e8348`。Phase 3 D1a [PR #43](https://github.com/Fugu0141/Meltia/pull/43) 後の小PR。[全体計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 3 / D1。

## 1. 問題・目標（要件定義）

`H10CharacterEvidence.Difference`は英語/日本語の各n-gram尤度を計算するたびに `ToLowerInvariant` と `"^^" + ... + "$"` をそれぞれ確保し、bigram/trigramの`Substring`と連結で大量の短期文字列を生成する。H10・H11は同じモデルを使い、H11は英語の部分区間を多数列挙するため、割り当てが大きいと考えられる。

**目的：** 合成・既知Issueに対するスコア・候補・全Top-K・例外を変えず、尤度計算で生成する文字列を削減し、回帰と測定で効果を検証する。

- 既存英語/日本語の固定seed・3-gram頻度・平滑化定数 **0.4 / 27**、ログの加算順、長さ正規化、`Math.Clamp(..., -2.0, 2.0)`は一切変更しない。
- `Difference(string)`を残し、内部`Difference(ReadOnlySpan<char>)`を追加する。ASCII入力の`ToLowerInvariant`とpaddingは仮想的な添字参照へ置き換える。辞書は既存`Dictionary<string,int>`から初期化時にbigram/trigramを可逆なASCII byte packed `uint`へコピーする。辞書の学習値・頻度を変えない。
- 非ASCII入力での.NET `ToLowerInvariant`の意味の差を避けるため、その経路は元の正規化・Lookupにフォールバックする。ASCIIの大文字小文字・記号・数値・空文字の結果は**浮動小数bitレベルで同一**を目指す。
- H10/H11の呼び出し元はrawの`(start,length)`からSpanで渡せるようにし、元の候補文字列を作る必要のある辞書/正規化/スコアリング箇所は維持する。ロック・辞書・ビーム・Top-K、Windows Preview/TSF、Releaseは変更しない。

## 2. 実装前の意味契約

旧`LogLikelihood(word, model)`は `normalized = "^^" + word.ToLowerInvariant() + "$"` を作り、`i=2..normalized.Length-1`で:
- `history = normalized[i-2..i]`
- `eventKey = history + normalized[i]`
- `hits = model.GetValueOrDefault(eventKey)`と`prefix = model.GetValueOrDefault(history)`
- `sum += Math.Log((hits + 0.4) / (prefix + 27 * 0.4))`

これと同じ順序・浮動小数演算を行い、raw長を長さの分母に使用する。rawが空の場合も `"^^$"` の1計算が存在する。rawが`null`の旧動作（`NullReferenceException`）を変えない。

## 3. 受け入れ・テスト

1. 旧尤度計算ソースを**テストに独立固定**してモデルの元辞書を読み、短いASCIIの完全列挙（0～3文字）・長短/大文字/数字/記号/非ASCIIのseed固定例で `Difference(string)`と`Difference(Span)`のDouble bit一致を検証する。辞書頻度の再学習は行わない。
2. `H10/H11`の固定47+56件（H11の103件完全フィンガープリント含む）・Core契約・長文/編集smoke・Windows Preview無フックselftest/Bridge・strict-qualityの全ゲートを変更なしでPASS。
3. 同じReleaseジョブ・同じ入力集合・同じプロセスで旧/新の割り当て量と時間をウォームアップ後に計測し、公開テストケースの比較値をこのMarkdownへ保存。数値は1ランの参考であり、全体性能や実入力の保証とみなさない。
4. mainへの統合はレビュー後。D2（ビーム保持改良）は別PRに分離する。

### 実行方法 (.NET10)

```sh
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --smoke --output artifacts/phase3-d1b-smoke.json
```

### 制限と後続課題

- 固定103件の指紋はH11専用。H10/従来Coreの全候補・局所Score・全Top-Kを固定したスナップショットは未整備。D1bはモデル尤度のみ最適化し、H10/H11の推論意図は変えない。
- キャッシュをユーザー入力間で永続化せず、入力ログや辞書を変更しない。
- Phase 0全長ベンチの1024 UTF-16で約233MB（H11）の観測値は同一環境の前後比較ではない。D1bのマイクロベンチの改善が、そのままH11全体で現れるとは限らない。
