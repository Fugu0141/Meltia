# H10 追加検証：Meltype Open / Closed 誤判定Issue（2026-10-14）

> **実施済みの新旧Core実測**。本書は診断用の手入力ラベルを基準とした**境界だけの評価**であり、Meltype実機の最終文字列・かな漢字変換・辞書品質・入力イベントの再現性を評価したものではない。IssueがClosedであってもMeltiaで直っているとは限らない。

## 検証対象・再現手順

- 新たに本家Meltypeの **40件のIssue** を参照して **56入力** を収録（収録時のIssue状態：Closed 48 / Open 8）。`issue_intents_extra_20261014.jsonl` にraw・出典URL・当時の状態・意図区間・注意事項を保存。
- これまでの47入力は変更せず、**合計103入力**を2系統で評価。両方とも学習やチューニングの元になり得る診断集合であり、独立holdoutとして扱わない。
- [GitHub Actions 実行 #38493713560](https://github.com/Fugu0141/Meltia/actions/runs/38493713560)・[2ファイルの比較JSON Artifact](https://github.com/Fugu0141/Meltia/actions/runs/38493713560/artifacts/11863330913)。新旧に同じ生入力を供給し、`MeltypeEnglish`予測・辞書・人手のGoldをスコア入力に与えない。
- 直接の入力文字列がない #379、#411、#412 などは**合成入力を「原報告」と偽らず除外**。#135（かな入力モード）、#365（「VRChat」への語彙置換）、#519 / #637（句読点幅）も境界スコアの対象外。#79・#82・#131等は日本語のみの**誤英語検出対照**として含めるが、ローマ字表記法・漢字変換の正しさは検査しない。
- 「buy me a coffee」の空白はEN区間外、「passwordにして」の「にして」は原文そのまま保持。#258のスペース表記は「Are you ningen?」へ正規化した**派生例**であり、確定済み文字列の書換えをCore単体では判定できない。
- `issue_state_at_collection` は現時点の状態の**スナップショット**で、更新された場合は後で取り直す。

### 実行方法

```bash
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --output artifacts/h10/h10-comparison.json
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --input tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl --output artifacts/h10/h10-extra-issue-comparison.json
```

## 新たに追加した56入力の比較

| 指標 | v1.0-flank-independent | H10-dual-channel |
| --- | ---: | ---: |
| Gold EN区間の候補完備（純JA13を含む） | 29/56 | **54/56** |
| **ENがある43例だけ**のGold EN候補完備 | 16/43 | **41/43** |
| Top-Kに正解の英語区間集合がある | 27/56 | **42/56** |
| Top1英語区間完全一致 | 15/56 | **26/56** |
| 境界F1 | 0.261 | **0.429** |
| **純JA13入力で誤EN出現** | **1/13** | 5/13 |

上の候補完備率は、GoldにEN区間が存在しない場合 `All` が真になるため、そのまま「英語が全部見つかった割合」として解釈しない。別記した **16/43→41/43** を英語正例に対する候補カバレッジとする。ただしH10は多数の部分区間を列挙するため、候補を増やした効果と識別能力は区別すべき。

### Issue状態・分類別

| 分類 | 件数 | 従来Top1 | H10 Top1 | 注意 |
| --- | ---: | ---: | ---: | --- |
| Closed報告 | 48 | 14 | **24** | 閉じたIssueの過去報告。現在のMeltype再現ではない |
| Open報告 | 8 | 1 | **2** | 現在進行形の問題として監視が必要 |
| EN-OOV（英字候補） | 18 | 1 | **10** | 単語途中の英語区間取り違えが残る |
| SEAM（英日混在） | 23 | 2 | **7** | 意図した「助詞の直前」で区切れない例多数 |
| JA-PROTECT（日本語負例） | 13 | **12** | 8 | **日本語保持で明確な退行** |
| STRUCT（識別子） | 1 | 0 | **1** | これだけでは一般化しない |
| CONTEXT（文脈・確定） | 1 | 0 | 0 | rawのみでは実際の確定書換え不具合を判定できない |

## ケース別の英語スパン比較（全56入力）

表の「期待EN」「H10予測EN」は**原文の中で英字として残すべき区間**。期待が「—」の場合は、日本語（または記号）として保持する入力である。英語の区間が合っていても、出力の漢字・カタカナが正しいとは限らない。❌の理由は候補生成欠落／経路探索／順位付けを区別する。

| 元Issue/状態 | 入力raw | 期待EN区間 | H10予測EN区間 | Top1 |
| --- | --- | --- | --- | --- |
| [#1](https://github.com/yksr-melt/Meltype/issues/1) / Closed | `kyouhameetinggaarimasu` | `meeting` | `—` | ❌ pruned-or-missing-ja |
| [#12](https://github.com/yksr-melt/Meltype/issues/12) / Closed | `feature` | `feature` | `feature` | ✅ |
| [#65](https://github.com/yksr-melt/Meltype/issues/65) / Closed | `anata` | `—` | `—` | ✅ |
| [#77](https://github.com/yksr-melt/Meltype/issues/77) / Closed | `reflectsareta` | `reflect` | `reflect` | ✅ |
| [#104](https://github.com/yksr-melt/Meltype/issues/104) / Closed | `moraltute` | `—` | `mor` | ❌ ranking |
| [#129](https://github.com/yksr-melt/Meltype/issues/129) / Closed | `AInituite` | `AI` | `—` | ❌ pruned-or-missing-ja |
| [#130](https://github.com/yksr-melt/Meltype/issues/130) / Closed | `50ccgenntuki` | `50cc` | `ccge` | ❌ missing-candidate |
| [#153](https://github.com/yksr-melt/Meltype/issues/153) / Closed | `hosuthingu` | `—` | `—` | ✅ |
| [#207](https://github.com/yksr-melt/Meltype/issues/207) / Closed | `pedia` | `pedia` | `pedia` | ✅ |
| [#207](https://github.com/yksr-melt/Meltype/issues/207) / Closed | `protopedia` | `protopedia` | `protopedia` | ✅ |
| [#207](https://github.com/yksr-melt/Meltype/issues/207) / Closed | `Pedia` | `Pedia` | `Pedia` | ✅ |
| [#218](https://github.com/yksr-melt/Meltype/issues/218) / Closed | `hotelltu` | `hotel` | `—` | ❌ pruned-or-missing-ja |
| [#218](https://github.com/yksr-melt/Meltype/issues/218) / Closed | `hotelltsu` | `hotel` | `llt` | ❌ pruned-or-missing-ja |
| [#218](https://github.com/yksr-melt/Meltype/issues/218) / Closed | `totalltute` | `total` | `—` | ❌ pruned-or-missing-ja |
| [#220](https://github.com/yksr-melt/Meltype/issues/220) / Closed | `tabde` | `tab` | `—` | ❌ ranking |
| [#220](https://github.com/yksr-melt/Meltype/issues/220) / Closed | `tabga` | `tab` | `—` | ❌ ranking |
| [#220](https://github.com/yksr-melt/Meltype/issues/220) / Closed | `tabwo` | `tab` | `—` | ❌ ranking |
| [#220](https://github.com/yksr-melt/Meltype/issues/220) / Closed | `tabni` | `tab` | `—` | ❌ ranking |
| [#254](https://github.com/yksr-melt/Meltype/issues/254) / Closed | `MeltypeTip.dll` | `MeltypeTip.dll` | `MeltypeTip.dll` | ✅ |
| [#266](https://github.com/yksr-melt/Meltype/issues/266) / Closed | `McAfeeto` | `McAfee` | `Mc` | ❌ ranking |
| [#300](https://github.com/yksr-melt/Meltype/issues/300) / Closed | `buy me a coffee` | `buy / me / a / coffee` | `buy / me / coffee` | ❌ missing-candidate |
| [#301](https://github.com/yksr-melt/Meltype/issues/301) / Closed | `opencode` | `opencode` | `opencode` | ✅ |
| [#303](https://github.com/yksr-melt/Meltype/issues/303) / Closed | `Wiz Khalifa` | `Wiz / Khalifa` | `Wiz / Khalifa` | ✅ |
| [#357](https://github.com/yksr-melt/Meltype/issues/357) / Closed | `handanwodelegatesuru` | `delegate` | `le` | ❌ pruned-or-missing-ja |
| [#368](https://github.com/yksr-melt/Meltype/issues/368) / Closed | `sha-nashi` | `—` | `—` | ✅ |
| [#369](https://github.com/yksr-melt/Meltype/issues/369) / Closed | `huihui` | `huihui` | `—` | ❌ ranking |
| [#376](https://github.com/yksr-melt/Meltype/issues/376) / Closed | `passwordにして` | `password` | `password` | ✅ |
| [#377](https://github.com/yksr-melt/Meltype/issues/377) / Closed | `gameover` | `gameover` | `meover` | ❌ ranking |
| [#378](https://github.com/yksr-melt/Meltype/issues/378) / Closed | `memoirs` | `memoirs` | `memoirs` | ✅ |
| [#405](https://github.com/yksr-melt/Meltype/issues/405) / Closed | `overcute` | `overcute` | `overcute` | ✅ |
| [#407](https://github.com/yksr-melt/Meltype/issues/407) / Closed | `tuidenifetch upstream` | `fetch / upstream` | `fetch / upstream` | ✅ |
| [#455](https://github.com/yksr-melt/Meltype/issues/455) / Closed | `oemnopasokonn` | `oem` | `oem` | ✅ |
| [#469](https://github.com/yksr-melt/Meltype/issues/469) / Closed | `holoX` | `holoX` | `loX` | ❌ ranking |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yagithub` | `github` | `github` | ✅ |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yapython` | `python` | `python` | ✅ |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yaplay` | `play` | `play` | ✅ |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yamada` | `—` | `—` | ✅ |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yasai` | `—` | `—` | ✅ |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yappari` | `—` | `—` | ✅ |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yaml` | `yaml` | `ml` | ❌ ranking |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yarn` | `yarn` | `rn` | ❌ ranking |
| [#440](https://github.com/yksr-melt/Meltype/issues/440) / Closed | `yahoo` | `yahoo` | `—` | ❌ ranking |
| [#145](https://github.com/yksr-melt/Meltype/issues/145) / Open | `discode` | `discode` | `discode` | ✅ |
| [#249](https://github.com/yksr-melt/Meltype/issues/249) / Open | `mozcgamiteirutohaie` | `mozc` | `mozcg` | ❌ pruned-or-missing-ja |
| [#268](https://github.com/yksr-melt/Meltype/issues/268) / Open | `DeVitode` | `DeVito` | `DeVitode` | ❌ pruned-or-missing-ja |
| [#268](https://github.com/yksr-melt/Meltype/issues/268) / Open | `DeVito` | `DeVito` | `DeVi` | ❌ ranking |
| [#316](https://github.com/yksr-melt/Meltype/issues/316) / Open | `Claudenokiougogamenntokadehennkannsarenaikotoaru.` | `Claude` | `Cl` | ❌ pruned-or-missing-ja |
| [#347](https://github.com/yksr-melt/Meltype/issues/347) / Open | `deepseek` | `deepseek` | `deepseek` | ✅ |
| [#348](https://github.com/yksr-melt/Meltype/issues/348) / Open | `Mainのコードで確認` | `Main` | `—` | ❌ ranking |
| [#654](https://github.com/yksr-melt/Meltype/issues/654) / Open | `ratelisu` | `—` | `ratel` | ❌ pruned-or-missing-ja |
| [#258](https://github.com/yksr-melt/Meltype/issues/258) / Closed | `Are you ningen?` | `Are / you` | `—` | ❌ pruned-or-missing-ja |
| [#82](https://github.com/yksr-melt/Meltype/issues/82) / Closed | `ichiriaru` | `—` | `—` | ✅ |
| [#147](https://github.com/yksr-melt/Meltype/issues/147) / Closed | `ageashitoruna` | `—` | `—` | ✅ |
| [#131](https://github.com/yksr-melt/Meltype/issues/131) / Closed | `cyenn` | `—` | `cyenn` | ❌ pruned-or-missing-ja |
| [#196](https://github.com/yksr-melt/Meltype/issues/196) / Closed | `syotakonn` | `—` | `syo` | ❌ ranking |
| [#79](https://github.com/yksr-melt/Meltype/issues/79) / Closed | `ci` | `—` | `ci` | ❌ ranking |

## 総合解釈・次の実装要件

1. **候補生成の改善は大きいが、正解位置へ切る精度は依然低い**。`tabde` / `tabga` 等は候補にあっても日本語が選ばれる。逆に `mozcgamiteirutohaie` や `Claudenokiou...` は誤った位置で切れる。
2. **英字短語・英数字・記号の統合が不十分**。`50ccgenntuki` では **`50cc` の全域候補がない**。`buy me a coffee` では一文字の `a` が候補化されない。候補生成を幅広くするだけでは直せない。
3. **日本語保護が悪化**。`moraltute`、`ratelisu`、`cyenn` など日本語入力が英語に吸収される。日本語のローマ字対応・未完成音節の許容と英語統計を分離したモデルが必要。
4. **本家IssueのClosed＝修正済みではない**。本家の報告当時の版・現在の版・Meltiaの別実装という三者は区別する。特に #220 は表示と確定時の`FixTypos`の食い違いで、境界Top1だけでは根治を検証できない。
5. **性能改善の必要性**。この同じ追加データにおける参考時間（各56入力×5回、Linux CI）は v1.0 の p50/p95/p99 = 0.0968 / 0.5076 / 2.7631 ms、H10 は **0.2027 / 1.3374 / 11.3974 ms**。ホストの実打鍵ではなく小規模診断で、GCやJITのばらつきも含む。長文と後戻り入力を含む独立ベンチで再測定する。
6. この診断結果に合わせて新しい条件分岐を次々付け足すのは避ける。次の研究は **JA/EN構造の正しい区間生成、過剰吸収しない順位付け、可逆的な境界不確実性** を改善し、学習と独立したholdoutで評価する。

## PR運用

**PR #34へ本書、56件診断コーパス、ランナー拡張、CI二系統のJSONをまとめる。** 旧CoreとWindows Previewは変更せず、mainへマージしない。診断が増えたことをもってH10の採用条件を満たしたと判定しない。
