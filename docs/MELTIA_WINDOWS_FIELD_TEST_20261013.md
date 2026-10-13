# Windows実機フィードバックに基づく境界修正・評価計画（2026-10-13）

## 1. 位置付け

PR #18/#20統合後main（`99075ee8b5c87b86f79d9cdac645fb1caad5027a`）をビルドしたWindows Keyboard Previewについて、手入力の報告を受けた**バグ修正**。次期版の未知英語モデルや学習方式を無検証で既定ONにはしない。正式TSF/IMEの入力E2Eは別ゲート。

## 2. 報告された事実と区別する仮説

| キー列 / 観測 | 実機結果（実験境界OFF） | 現時点の原因仮説・扱い |
| --- | --- | --- |
| `commitha` | 「こっみてゃ」 | English `commit` の最後の `t` と助詞 `ha` が `tha` のローマ字かな単位に結合され、英語境界が消える可能性。単位分離を修正し回帰追加 |
| `Adoさんの`（入力例 `Adosannno`） | `Adosannno` のまま | アーティスト名 `Ado` が同梱固有名詞辞書にない。人手で用途が明確な固有名詞だけ候補辞書に追加 |
| `API` | テスト報告はNG（具体的表示不明） | 自動テストでRaw/表示を監視。異なる再現環境での確定結果はまだ不明とする |
| `meltiaga` | 入力途中に一時的な表示不良 | 各打鍵のRaw・区間完全被覆を検査。ただしGUIの描画フリッカーやキーイベント順序は自動セルフテストでは再現できない |
| `seeyouagain`, `IME`, `meltiawotamesu`, `Meltiawotamesu`, `kyouhagoogledekensaku`, URL | 報告ではOK | 修正で壊さない回帰対象。文字列が曖昧なら文脈/設定の異なる解釈を独立評価する |

## 3. 最小変更・受け入れ条件

1. 英語の最後の `t` に `ha` が続く場合、既知英単語として独立していた `t` を `tha`（てゃ）に再結合させない。無関係な日本語 `tha`、未知英字、単語内の綴りを無条件に英語固定しない。
2. 独立な原著データの学習ではなく、既存の `propernouns.txt` に人手の正解例として `Ado` を1語追加。**大量の機械生成例は正答学習データではない**。後続の固有名詞追加は独立したコーパスと負例で検証する。
3. `CapitalizedWordEnd` の全後続文字列の走査を、入力冒頭が大文字か小文字→大文字（例: iPhone）の時だけ行うようにする。大文字固有名詞の既存規則、UTF-16、手動補正、Raw保存を維持。
4. Preview self-testで `commitha`、`Adosannno`、`API`、`meltiaga` を追加し、OS横断CoreテストとWindows Preview publish + smokeを走らせる。**自動テストの成功だけでは実入力E2E合格にしない**。

## 4. IncrementalBoundaryLabから適用した設計判断

参照: [Phonetic-first](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/docs/phonetic-first.md)、[OOV研究（2026-10-10）](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/research/2026-10-10-oov-character-candidates.md)、[CI](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/docs/ci-validation.md)。

- 音の読みを作る**Stage 1**と英語/日本語の区間根拠**Stage 2**を混ぜない。今回は先に単位境界を保証し、その後既存英語辞書/固有名詞の根拠で区間を選ぶ。
- 研究の54未知英語語テストでは文字モデル候補が固定語尾候補より多く発見されたが、**候補recallはWindows IMEの正答率ではない**。日本語だけの負例でもseedによって誤候補が生じるため、学習器を直接確定処理に接続しない。
- `tokyo`/`ime` のように意図不明な同字列は原文だけでは決定できない。大文字、利用者の明示修正、既存固有名詞、前後文脈を追加の証拠にする。誤確定は増やさない。
- 未知英語の学習データについては著作権・ライセンス/holdout/ラベル品質の監査が必要。今回の変更は研究データのモデル重みや外部辞書を転載しない。

## 5. 性能測定・比較条件

今回の早期終了は、普通の小文字ローマ字を入力する度に `CapitalizedWordEnd` が全残り区間を読み直す仕事を省くもの。**速くなる量はまだWindows実機で計測していない**。旧mainと修正ブランチを同じPC/SDK/Release構成で順番を入れ替えて比較すること。

```powershell
dotnet run --project tools/performance/Benchmark.csproj -c Release -- 15 32,128,256
```

このベンチマークは `tools/performance/README.md` に記載されたセッションのp50/p95/max、割り当てbytesと入力全体の経過時間の検証用。ウォームアップ・ランナー負荷・辞書版・文字数・SHAを揃え、両方の出力ハッシュも一致させる。*手動の1打鍵遅延・描画/UI更新・キー再送の遅延はここでは測定できない*。

## 6. Windows手動再検証

1. **最新版ブランチからローカルで再ビルド**し、実験境界OFFでメモ帳に `commit`, `commitha`, `API`, `Adosannno`, `meltiaga`, `meltiawotamesu` を入力。途中表示とEnter確定後の結果を別々に記録。
2. 実験境界ONで同じ入力と `kyouhacommitsimasita` / `githubdeissue` / `kyouhagoogledekensaku` を比較。これらはIncrementalBoundaryLabの研究例に含まれるが、Windowsでの正答を保証しない。
3. Backspace・前後の入力欄フォーカス移動、連続入力、長文の末尾編集を同条件で比較。パスワード欄・個人情報・重要書類は使用しない。
4. 精度は原文保持率/区間完全一致/日本語・英語誤判定/誤確定、性能は短文32字・中間128字・長文256字の平均/p95/p99/allocを**別々に**報告する。

## 7. 未実施・合格の宣言範囲

この文書作成時点では、修正後のWindows GUI実キーE2E、パフォーマンスのbefore/after実測、ラベル付き自然文holdoutの比較は未実施。追加セルフテスト/CIが通ってもリリース不可という従来のゲートを維持する。

## 8. ユーザー提供のWindowsローカル性能ログ（2026-10-13、初回分析）

**入力資料**: `dotnet run --project tools/performance/Benchmark.csproj -c Release -- 15 32,128,256` の標準出力180行。4シナリオ（`long_url`, `natural_mixed`, `mixed_repeated`, `japanese`）× 32/128/256文字 × 各15試行。計測時のGit HEAD/CPU/SDK詳細はログに**含まれない**ため、修正後ブランチのbefore/after比較や一般的な機械への外挿には使わない。

### 記録値

`total_ms` は15回のシナリオ総時間の中央値、`alloc_MiB` は中央値のスレッド累積割り当てバイト数を1024²で換算、`key_p95_ms` は各試行の打鍵処理p95の中央値。最後の列は15試行中の最大の単一打鍵時間（値の合成方法が異なることに注意）。

| ケース | 文字数 | total_ms | alloc_MiB | key_p95_ms | 最大の単一打鍵 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| long_url | 32 | 1.019 | 0.330 | 0.044 | 6.657 |
| long_url | 128 | 14.256 | 3.033 | 0.178 | 1.115 |
| long_url | 256 | 13.006 | 10.792 | 0.097 | 0.590 |
| natural_mixed | 32 | 4.717 | 1.233 | 0.271 | 6.257 |
| natural_mixed | 128 | 39.294 | 19.114 | 0.751 | 2.100 |
| natural_mixed | 256 | 311.580 | 133.447 | 3.376 | 7.565 |
| mixed_repeated | 32 | 3.368 | 0.809 | 0.189 | 0.652 |
| mixed_repeated | 128 | 38.229 | 20.109 | 0.784 | 2.202 |
| mixed_repeated | 256 | 604.823 | 173.739 | 7.165 | 22.558 |
| japanese | 32 | 5.714 | 1.442 | 0.399 | 1.910 |
| japanese | 128 | 57.786 | 37.238 | 1.049 | 3.833 |
| japanese | 256 | 598.202 | 194.634 | 5.874 | 42.307 |

### 分析・注意

- **再現性**: 各条件15試行の確定出力SHA-256は条件内で一致（12/12条件、計180件）。これは出力の*決定性*を示し、自然言語としての正解率や実キーの描画安全性を示さない。異なる入力長のハッシュが違うのは当然。
- **長さに対して超線形の伸び**: 128→256文字で総時間は `natural_mixed` 約7.93倍、`mixed_repeated` 約15.82倍、`japanese` 約10.35倍。累積割り当てもそれぞれ約6.98/8.64/5.23倍。キャッシュ更新、区間探索、正規化、外部JSON化などのどの部分が主因かは、このログのみでは断定できない。
- **長文の外れ値**: `japanese` 256字はある試行で1キー42.307ms、`mixed_repeated` 256字は最大22.558ms。実入力では引っ掛かりに感じる可能性があるが、実GUIの描画時間ではない。JIT・GC・スケジューリング・OS負荷との切り分けも未完。
- **URLの処理差**: `long_url` 128字14.26ms/256字13.01msは、長さに単調でない。コード上の保護区間・JIT/環境変動・入力状態の違いを検証するまで原因は推定にとどめる。
- **ハーネス範囲**: `HandleKey`、結果のJSONシリアライズ、Enter確定を計測する。実Windowsフックや候補UI、ホストアプリのフォーカス・描画は測らない。割り当ては`GC.GetAllocatedBytesForCurrentThread`の累積であり、アプリの常駐メモリ量ではない。
- **比較の不足**: 入力ログが1ビルド分でベースラインがないため、「最適化によって何％速くなった」は算出不能。次回は比較対象の2つのコミットSHA、CPU/OS/.NET、ユーザー辞書・設定、コマンド、各出力SHAを併記する。

### 次の調査

1. 同じWindows端末で `99075ee` とPR #24 HEADを比較し、前後の**出力同一性**と性能を検査。意味のある修正対象のケースでは出力ハッシュ差を許容し、意図した差分を別の正解oracleに照合する。
2. `CompositionDetector.FindSpans` の区間候補ループ、`Prepare` の全長再構築、`CompositionText.SegmentsKey`、`Normalize`、JSONシリアライズそれぞれの時間/割り当てを分けてプロファイルする。長文での繰返し全体走査を第一候補として調べる。
3. 正解付き日英混在holdout/未知語負例と10,000件入力マトリクスを維持し、p50/p95/p99・割り当てと正解率を**独立のゲート**で判断する。

**ステータス**: このログの単一ビルドについて出力ハッシュの再現性を確認。長文性能には課題あり。Windows実キーE2Eと旧版比較は未確認。
