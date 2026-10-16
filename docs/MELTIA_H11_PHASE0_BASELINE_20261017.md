# H11 Phase 0-A1：Issue診断103件の不変ベースラインと再現性ゲート（2026-10-17）

> **研究用テストの強化。H11/H10/従来Coreの推論コード、辞書、Windows Preview/TSFを変更しない。**
> [次期開発全体計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md)のPhase 0「ベースラインと評価方法の固定」の**最初のPR**。Phase 0の長文ベンチ・独立holdout整備は別PRで実装する。

## 1. なぜ必要か

H11はMeltype Issue由来103件でTop1 **72/103**、Top-K **94/103**、非EN21件の誤英語 **1/21** だった。ただし既知Issue診断であり、実際の一般入力の正解率ではない。候補・英日境界・スコアの意図しない変化を捉えないままSpan化や共通DP化をすると、総Top1が同じでも別の文章で退行し得る。

今回凍結するものは**現在のH11実装の振る舞い**であり、「正しい日本語・英語の唯一の正解」ではない。既知の**31件の誤判定も**そのまま記録する。後続の**挙動不変リファクタリング**でこれらまで勝手に変わらないことを確認し、**精度改善PR**では意図した差分のみ理由を記録して基準を更新する。

## 2. 基準ソースと集計

| 資料 | 固定位置 |
| --- | --- |
| 研究用H11をmainへ取り込んだPR | [#34](https://github.com/Fugu0141/Meltia/pull/34) |
| このPhase 0に着手したmain | `1250bcf0bc8c69b0542e5ec7d840e5927292d052`（計画PR #35のmerge） |
| 診断データA（47件） | `tests/Meltia.H10Evaluation/issue_intents_v2.jsonl` |
| 診断データB（56件） | `tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl` |
| H11用既存英語辞書 | `dictionaries/english.txt`（埋込リソース `Meltia.H11EnglishWords`） |
| 精度の正本 | [H11研究結果](MELTIA_H11_RESEARCH_RESULTS_20261014.md) |
| 固定スナップショット | `tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl` |
| スナップショット生成・検証 | `tests/Meltia.H10Evaluation/H11Snapshot.cs`、既存`Program.cs` |
| 再実行 | `.github/workflows/meltia-h10-prototype.yml` |

両データセットの元のrawとgoldを変えず、評価時の推論にはgold・Issue ID・正解を**一切供給しない**。同一raw別意図・重複報告は元データのとおり保持する。

## 3. 記録・比較する項目

ケースごとに `Corpus` / `Id` / `CorpusSha256` / `RawSha256` / `CandidateAndRouteSha256` / `ScoresSha256` / `Top1` / `TopK` / `EdgeCount` / `EnglishCandidateCount` / `NeedsConfirmation` / `AutoCommitAllowed` を保存する。

- **候補・経路の指紋**：候補のUTF-16開始・終了、言語、出自、列挙順、全Top-Kの英字スパンと経路順、不確実性/自動確定フラグをSHA-256に含める。ルートの文字区間だけ変わらない「見えない退行」も検知する。
- **得点の指紋**：各候補・各ルートの`double.ToString("R", InvariantCulture)`を同順で別SHA-256にする。浮動小数点演算の順序や実装差を厳しく検出する（機種間で違うときは**黙って再生成せず、根拠と比較結果を調べる**）。
- **生データ・辞書の出自**：rawはUTF-8のSHA-256。診断コーパスはCRLFをLFへ正規化して計算。H11が実際に読み込む埋込辞書のバイト列SHA-256をレポートに記載し、辞書版の取り違えを防ぐ。
- **公開性**：ゴールド固定ファイルには個人のキー入力・ユーザー設定や生の秘密情報を保存せず、既に公開されたIssueのID/ハッシュ/英字スパンのみ扱う。プライベート入力で`--emit-baseline`を使わない。
- **評価と測定の分離**：従来の参考p50/p95/p99は引き続き生成するが、変動する経過時間は決定的なgoldの比較に入れない。CIのジョブでのタイミングをWindowsの遅延や精度と混同しない。

## 4. 実行方法（.NET 10 / repo root）

```powershell
# 既存Coreの回帰
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release

# 47件：全候補・Top-K・スコア・確認要否が固定値と完全一致すること
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --output artifacts/h11/old47.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl

# 追加56件：同上
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --input tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl --output artifacts/h11/extra56.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
```

CIは各ケースIDの一致、コーパスhash、raw hash、候補/経路hash、得点hash、Top1/Top-K/件数/フラグを比較し、**1件でも異なれば失敗**する。差分があれば自動更新しない。

### 基準更新の手順（精度の意図的変更のみ）

1. 別PRで新アルゴリズムまたは理由がある動作修正を提案し、**変更前後のgold103件・保護負例・独立holdout**を比較。
2. 研究用固定入力のみで `--emit-baseline` を実行し、`H11_SNAPSHOT`行を抽出。対象コミット、辞書ハッシュ、元コーパス、レビュー根拠を記録する。
3. **変更の理由と前後の英字区間の差分**を示し、レビュー済みの値だけをコミットする。原則、過去の基準ファイルは履歴に残し、versionを増やす。
4. 決定性のない変更や計測環境の違いによるスコア差は、再現条件を先に特定する。

## 5. このPRと今後のPhase 0残作業

**このPRで実装する受け入れ条件**：47/56両方のスナップショット生成をCIで確認し、その実測を固定した103行の基準ファイルをコミット。CIでは通常の比較実行で固定値と照合して成功を確認。元のTop1・Top-K・誤EN数が変化していないことを再実行ログで確認する。

**このPRには含めない（Phase 0-A2以降）**：英語/日本語混在の32/128/256/512/1024文字負荷、データ割り当て測定、端末別CPUのばらつき、編集列とフル再解析の性能比較、独立自然文holdoutの収集・ラベル監査、辞書非命中の層別評価。これらを検証済みとして記載しない。

## 6. 独立したセキュリティゲート

[PR #30](https://github.com/Fugu0141/Meltia/pull/30) / [Issue #23](https://github.com/Fugu0141/Meltia/issues/23) は別対応であり、このPhase 0では旧GitHub Releaseワークフローの設定・過去タグ・アセットを変更しない。研究CIの成功を、旧Linux公開経路の安全性の証明に使わない。
