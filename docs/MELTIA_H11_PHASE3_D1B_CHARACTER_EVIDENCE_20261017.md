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

## 5. 追加テストと初回CI結果（2026-10-17）

### 実装・原因分析と修正

初回HEAD `cd3337c549e7019312fac59a11856a4006c33c6e` のCIは **コンパイル失敗**。 `VirtualChar` の末尾パディング文字を返す行がソース更新時に壊れ、`CS1010 Newline in constant`等の文法エラーを起こした（例：[初回Core契約 #38992371466](https://github.com/Fugu0141/Meltia/actions/runs/38992371466)）。n-gram判定の不一致ではなく、不正なソース文字列が原因。

修正コミット `3ef492122e20c6b1ba76b6ae0f36d335cb65d348` では末尾パディングを明示的に`(char)36`（ASCII dollar sign）で記述。元コードの`"^^" + lowercase + "$"`と同じ入力列を作る。**エラーを隠すためにテストを減らしたり品質ゲートを弱めたりしていない**。

### 再現可能な同値確認

修正HEAD：[Core boundary diagnostics #38992430847](https://github.com/Fugu0141/Meltia/actions/runs/38992430847) **ALL PASS 224**。

- `Phase3D1bCharacterEvidenceTests`は旧 `LogLikelihood` の式・加算順を固定し、同じseed Build辞書を使って新 `Difference(string)` と新 `Difference(ReadOnlySpan<char>)` との **`DoubleToInt64Bits`完全一致**を確認。
- ASCII 0～3文字の **18,279件全列挙**、固定seedの大小文字・数字・記号・NUL入り3,000件（1024/1025長も含む）と、非ASCII、日本語、U+0130などUnicode case folding、絵文字/孤立サロゲート、null時の例外を確認。代表的な混在入力も固定。
- 0長rawでもバイグラム`^^`→`$`を一回処理し、事前文字列版と同じ長さ正規化と`Math.Clamp`。
- [H10比較・H11凍結103件 #38992430756](https://github.com/Fugu0141/Meltia/actions/runs/38992430756)は成功、H10は元の47件`Top1 28/47`、追加56件`26/56`、H11は47件`32/47`、追加56件`40/56`。H11は47+56の **全103件の指紋一致**であり、集計値一致だけではない。

### 同一ジョブ内の簡易マイクロベンチ

GitHub Actions Linux runner、.NET10 Release、公開合成文字列12個×反復1,200回（計14,400回/方式）。1プロセス、各関数3回ウォームアップ、実行前GC、`Stopwatch`と現在スレッドにおける`GC.GetAllocatedBytesForCurrentThread`を使用。

| 指標 | 変更前string尤度アルゴリズム | D1b Span / packed |
| --- | ---: | ---: |
| 割り当て量 | **19,468,840 bytes** | **40 bytes** |
| 処理時間 | **25.594 ms** | **9.866 ms** |

**解釈**：大幅に減っているのはこの**単体関数の**割り当てであり、ビーム・Candidate、辞書照合・長文の総コストとは分けて扱う。独立した全件bit一致を確認した。ただし初回ベンチの偶数回XORチェックサムは差分検出に使えなかった（第6節で修正）。1回のベンチで速度改善の有意性や実Windows打鍵の改善率は主張できない。40 bytesは計測/ランタイム等の微小オーバーヘッドが含まれる可能性があり「完全な0 allocation」を示す値ではない。

### その他のCI

- [H11 Phase1 DiagnosticTrace #38992430718](https://github.com/Fugu0141/Meltia/actions/runs/38992430718)：SUCCESS。
- [H11 synthetic benchmark smoke #38992430864](https://github.com/Fugu0141/Meltia/actions/runs/38992430864)：SUCCESS。
- [Input matrix #38992430854](https://github.com/Fugu0141/Meltia/actions/runs/38992430854)：SUCCESS。
- [Boundary quality #38992430897](https://github.com/Fugu0141/Meltia/actions/runs/38992430897)：SUCCESS。
- [pr-checks #38992430827](https://github.com/Fugu0141/Meltia/actions/runs/38992430827)：SUCCESS。
- [strict-quality Windows/Ubuntu #38992430787](https://github.com/Fugu0141/Meltia/actions/runs/38992430787)、[Windows Preview CI #38992430874](https://github.com/Fugu0141/Meltia/actions/runs/38992430874)：最終確定時に両方成功したことを別途確認する。

H11の全長1024文字に対する同一ハードウェアの前後測定、独立自然文holdout、IME実キー入力E2E、H10の全候補fingerprintは未実施または未整備。これらはCI成功の説明に混ぜない。

## 6. PR #44レビュー対応：偶数回XORによるベンチ検証欠陥（2026-10-17）

指摘：[PR #44インラインレビュー](https://github.com/Fugu0141/Meltia/pull/44#discussion_r4300831572)（P2）。測定ループで `checksum ^= DoubleToInt64Bits(score(sample))` としていたが、同じ決定的なスコアを偶数回（1,200回）処理すると、XORの自己打ち消しにより**旧方式・新方式の両チェックサムが常に0になる**。したがって初回「ベンチのチェックサム一致」は正しさの証拠にならない。この点は本項で訂正する。

### 修正方法と選定理由

- `ulong`による**順序依存の64ビットFNV-1a型ローリングハッシュ**へ変更。初期値 `14695981039346656037`、更新は `unchecked((checksum ^ (ulong)DoubleToInt64Bits(score)) * 1099511628211)`。各サンプルと反復を順序どおり畳み込む。旧・新それぞれ同じ処理順でハッシュ化する。偶数回というだけで消滅するXOR欠陥を解消する。
- 新しい回帰テストでは**1,200回の偶数反復**で、同一のスコア配列を複製した場合のハッシュ一致と、**1要素だけ意図的に変えた場合の不一致**を明示的に検査する。初期値とは異なることも確認し、「何でも一致する」テストを防止する。
- 旧`LogLikelihood`由来の独立オラクルと新`Difference(string / Span)`との**bit単位一致**を確認する全18,279＋3,000例のテストは維持する。ローリングハッシュはこの直接比較の代用ではない。
- 変更は `tests/Meltia.Core.Tests/Phase3D1bCharacterEvidenceTests.cs` と本研究Markdownに限定。スコアリング本体、固定辞書、ビーム、Windows Preview、Releaseを変更しない。

### 過去の計測値の扱い・再測定

上の第5節に記録した `25.594 ms / 9.866 ms` と `19,468,840 / 40 bytes` は**修正前のチェックサム実装で計測した履歴値**。スコア自体の計測はできていたが、XORチェックサムによる旧新同一性の担保は無効である。修正後は各スコアbitの独立比較と、改善したローリングハッシュによる検証を別々に実行する。計測処理の演算数が変化するので、**修正後msを修正前msと直接比較して性能変化を論じない**。CI再実行と実測値を確認してから追記する。

### レビュー修正後の初回実測

- 修正対象コードと本節より前の記録を含むコミット `f3c1c8ce71e50c46e56259f395aded0eeeb0b1a7`： [Core契約 #38993601824](https://github.com/Fugu0141/Meltia/actions/runs/38993601824) **ALL PASS 225**（前回224＋「偶数反復でも変更されたスコアを検出する」1チェック）。旧尤度とのbit一致、独立オラクルのテストも継続成功。
- 同じCIジョブの `PHASE3_D1B_EVIDENCE_ALLOC`：合成短文12個×1,200反復、旧方式 **19,468,840 bytes / 13.384 ms**、新方式 **40 bytes / 4.364 ms**。
- 元のマイクロベンチの `25.594 ms / 9.866 ms` は**旧XOR蓄積を含む初回測定値**。今回の `13.384 ms / 4.364 ms` は**ローリングハッシュ版の別ジョブ測定値**であり、前後の数値自体を同一負荷条件として横断比較しない。旧新いずれも**同じ修正後ジョブ内**でのみ相対的に読める。ランナー間のノイズと順序効果を含む単回観測なので性能SLAや全体高速化の証明ではない。
- 新しい `Fingerprint` の正負例は同じスコア配列の複製が一致し、1つのスコアだけ変えると不一致になり、かつ初期値にも留まらないことを確認。旧方式は決定的な計算・偶数反復なら常に0になるため、この負例を満たせなかった。

各種CIの最終状態はPRのChecks欄で確認し、Windows実キーE2Eは未実施のままとする。
