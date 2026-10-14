# H11 試作結果と残る課題（2026-10-14）

> PR #34の独立研究用Coreで実装済み。**正式なMeltia Windows Preview / TSF / Meltype本家の入力処理には接続していない。** MeltypeのIssueで報告されたrawについて英語区間の一致を検査しており、漢字・かな・カタカナの最終変換結果を検証したものではない。

## 再設計の意図と実装範囲

[H11修正計画](MELTIA_H11_JOINT_SEGMENTATION_PLAN_20261014.md)に従い、H10の「すべての部分英語区間を大量列挙して局所スコアで上位を決める」方式から、**英語の語候補と日本語の音韻単位の同時Viterbi分割**を別エンジンとして試作した。

- **独立JA候補**：ローマ字の完全な音節列と、不正打鍵が最大1か所ある短い近似音節列を可逆な候補として扱う。`ci`、`cye/cyu/cyo`の代替ローマ字も、rawを書き換えずに候補上で読み取る。単独日本語の誤英語化を抑える。
- **独立EN候補**：固定の文字bi/tri-gramに加え、**この実験以前から存在した** `dictionaries/english.txt`を読み取り専用の埋込リソースとして使う。辞書は候補スコアの証拠であってロックではない。単語結合・一般的な接頭辞・CamelCase・大文字略語を独立に評価する。
- **英日継ぎ目**：助詞／接尾語のローマ字開始を検出し、短い英語の末尾子音から日本語へ移行する場合のコストを設計する。`50cc`など数字を含む英字語、独立した1文字語、URL・メール・識別子を扱う。
- **安全な探索**：言語状態別の幅制限付きDP、UTF-16のサロゲートペア保護、重複した保護範囲の処理、矛盾する言語ロックの拒否、Top-Kの出力。`AutoCommitAllowed`は常に`false`。
- **モデル区別**：既存Meltia v1.0とH10は変更していない。H11の改善には**方式と既存辞書の両方**が含まれ、方式単独の寄与率を分離していない。

## 実測（GitHub Actions / .NET 10）

[最新の検証ジョブ](https://github.com/Fugu0141/Meltia/actions/runs/38497251029) ／ [103ケース全件のJSON Artifact](https://github.com/Fugu0141/Meltia/actions/runs/38497251029/artifacts/11864760534)。47件と追加56件は従来のまま別々に採点した。いずれも**既知のIssue診断事例**であり、人手で別途確保した独立holdoutではない。

| 境界評価 | v1.0 | H10 | **H11** |
| --- | ---: | ---: | ---: |
| 47ケース Top1 | 12/47 | 28/47 | **32/47** |
| 追加56ケース Top1 | 15/56 | 26/56 | **40/56** |
| **累計 Top1完全一致** | **27/103** | **54/103** | **72/103** |
| **累計 正解がTop-Kに存在** | 47/103 | 80/103 | **94/103** |
| ENを含む82ケースでgold EN候補が全部存在 | 30/82 | 80/82 | **82/82** |
| **非ENの21ケースで誤EN検出** | **1/21** | 8/21 | **1/21** |
| 境界F1：旧47ケース | 0.296 | 0.372 | **0.549** |
| 境界F1：追加56ケース | 0.261 | 0.429 | **0.720** |

**H10比でTop1が18件改善**。ただし正解はIssue由来の103ラベル付きケース・99種類のraw・80件の異なるIssueについてであり、**一般利用での正解率69.9%などと表現してはならない**。`new`/`aiueo`の同一raw異意図ペアや重複Issueが存在する。

| 分類 | H10 | H11 |
| --- | ---: | ---: |
| 追加56のClosed（48）Top1 | 24/48 | **35/48** |
| 追加56のOpen（8）Top1 | 2/8 | **5/8** |
| 追加56のSEAM（23）Top1 | 7/23 | **16/23** |
| 追加56のJA-PROTECT（13）Top1 | 8/13 | **13/13** |
| 追加56のEN-OOV（18）Top1 | 10/18 | 10/18 |
| 旧47のSEAM（15）Top1 | 5/15 | **8/15** |
| 旧47のJA-PROTECT（6）Top1 | 4/6 | **6/6** |
| CONTEXT（3）Top1 | 0/3 | 0/3 |

Closedは**当該本家Issueが閉じられている**という意味で、現在のMeltypeやMeltiaでの再現確認ではない。

### 具体的な改善例

`githubya`の「github/ya」、`tabde`の「tab/de」、`AInituite`の「AI/nituite」、`50ccgenntuki`の「50cc/genntuki」、`MeltypeTip.dll`の英字保持が正しいTop1になった。`mendokuseena`、`kedomala`、`moraltute`、`ratelisu`、`sha-nashi`などは日本語入力として保護できた。いずれも**境界ラベルの一致**であり、文字列が望みどおりの漢字・カタカナになる検証ではない。

## 未解決の31ケース：実測の原文とTop1英字区間

| 本家Issue | raw | 期待英語区間 | H11 Top1英語区間 | 失敗段階 |
| --- | --- | --- | --- | --- |
| [#536](https://github.com/yksr-melt/Meltype/issues/536) | `fatigue` | `fatigue` | `—` | ranking |
| [#506](https://github.com/yksr-melt/Meltype/issues/506) | `enumerate` | `enumerate` | `—` | ranking |
| [#346](https://github.com/yksr-melt/Meltype/issues/346) | `fusion` | `fusion` | `—` | ranking |
| [#485](https://github.com/yksr-melt/Meltype/issues/485) | `imaopenno` | `open` | `—` | ranking |
| [#154](https://github.com/yksr-melt/Meltype/issues/154) | `abctodef` | `abc / def` | `bctodef` | ranking |
| [#421](https://github.com/yksr-melt/Meltype/issues/421) | `kachiwodefinesuru` | `define` | `fine` | ranking |
| [#419](https://github.com/yksr-melt/Meltype/issues/419) | `mondaiwodefusesuru` | `defuse` | `—` | ranking |
| [#422](https://github.com/yksr-melt/Meltype/issues/422) | `mudawodecluttersuru` | `declutter` | `mud / clutter` | pruned-or-missing-ja |
| [#546](https://github.com/yksr-melt/Meltype/issues/546) | `betunisoreGitHubdeii` | `GitHub` | `bet / GitHub` | pruned-or-missing-ja |
| [#499](https://github.com/yksr-melt/Meltype/issues/499) | `sbisinnseiginnkou` | `sbi` | `sbis` | pruned-or-missing-ja |
| [#265](https://github.com/yksr-melt/Meltype/issues/265) | `Are you susi?` | `Are / you` | `—` | pruned-or-missing-ja |
| [#265](https://github.com/yksr-melt/Meltype/issues/265) | `Are you sushi?` | `Are / you` | `—` | pruned-or-missing-ja |
| [#350](https://github.com/yksr-melt/Meltype/issues/350) | `official髭男dism` | `official / dism` | `official / sm` | ranking |
| [#492](https://github.com/yksr-melt/Meltype/issues/492) | `new` | `—` | `new` | ranking |
| [#491](https://github.com/yksr-melt/Meltype/issues/491) | `aiueo` | `aiueo` | `—` | ranking |
| [#207](https://github.com/yksr-melt/Meltype/issues/207) | `protopedia` | `protopedia` | `—` | ranking |
| [#218](https://github.com/yksr-melt/Meltype/issues/218) | `hotelltu` | `hotel` | `—` | ranking |
| [#218](https://github.com/yksr-melt/Meltype/issues/218) | `hotelltsu` | `hotel` | `tell` | pruned-or-missing-ja |
| [#218](https://github.com/yksr-melt/Meltype/issues/218) | `totalltute` | `total` | `—` | ranking |
| [#266](https://github.com/yksr-melt/Meltype/issues/266) | `McAfeeto` | `McAfee` | `McAfeeto` | ranking |
| [#357](https://github.com/yksr-melt/Meltype/issues/357) | `handanwodelegatesuru` | `delegate` | `le` | pruned-or-missing-ja |
| [#369](https://github.com/yksr-melt/Meltype/issues/369) | `huihui` | `huihui` | `—` | ranking |
| [#377](https://github.com/yksr-melt/Meltype/issues/377) | `gameover` | `gameover` | `meover` | ranking |
| [#455](https://github.com/yksr-melt/Meltype/issues/455) | `oemnopasokonn` | `oem` | `—` | ranking |
| [#469](https://github.com/yksr-melt/Meltype/issues/469) | `holoX` | `holoX` | `loX` | ranking |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) | `yaml` | `yaml` | `ml` | ranking |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) | `yahoo` | `yahoo` | `—` | ranking |
| [#145](https://github.com/yksr-melt/Meltype/issues/145) | `discode` | `discode` | `iscode` | ranking |
| [#268](https://github.com/yksr-melt/Meltype/issues/268) | `DeVitode` | `DeVito` | `DeVit` | pruned-or-missing-ja |
| [#268](https://github.com/yksr-melt/Meltype/issues/268) | `DeVito` | `DeVito` | `DeVit` | ranking |
| [#258](https://github.com/yksr-melt/Meltype/issues/258) | `Are you ningen?` | `Are / you` | `—` | pruned-or-missing-ja |

英語区間が空欄の場合は「—」。`pruned-or-missing-ja`は英語候補自体が存在しても日本語側の経路が脱落する場合を含む。候補の欠落・経路の脱落・順位付けの失敗を混同しないこと。

## 処理時間と採用判断

同じLinux GitHub hosted runnerの47×5回、56×5回の独立`Analyze`の参考値（JIT/GCのノイズを含み、実キーボードの入力遅延ではない）。

| p95 ms | v1.0 | H10 | H11 |
| --- | ---: | ---: | ---: |
| 47ケース | 0.334 | 0.626 | **0.958** |
| 追加56ケース | 0.318 | 1.018 | **1.499** |

**現時点のH11はH10より遅い。** 研究用Top1が改善しても、逐次入力へそのまま採用しない。候補生成の計算量削減、増分更新、長文・入力編集を測る独立ベンチが必要。

`Are you susi?`などの文脈判断、`new`の同一raw別意図、辞書変換・かな漢字変換・確定後の再書換えによる重複や文字欠落は、境界Coreだけでは完了しない。ユーザーの意図と前後文脈のモデル、ホスト入力イベントの再現が必要。

## 検証・運用状況

`tests/Meltia.H10Evaluation/Program.cs`には3モデル比較と、H11に対してUTF-16、URL/メール、明示EN/JAロック、決定性、160ケースの**ラベルなしランダム不変条件テスト**を追加した。旧Core契約テストは引き続き成功。ラベルなしテスト160件を「160件の精度実測」として加算しない。

**PR #34のみ更新。既存Preview・本家Meltype・別PRの入力ロジックは変更せず、mainへはマージしない。** 次は未知の自然文による独立holdoutとストリーミング性能、Windows実キーE2Eを評価して正式な採用を判断する。
