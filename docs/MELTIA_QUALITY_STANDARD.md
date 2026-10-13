# Meltia 厳格品質検証標準 v1.0

研究元: [IncrementalBoundaryLab](https://github.com/Fugu0141/IncrementalBoundaryLab) の [CI検証](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/docs/ci-validation.md)・[研究データ仕様](https://github.com/Fugu0141/IncrementalBoundaryLab/blob/main/docs/research-data-schema.md)。本書は研究プロトタイプの測定結果をMeltia実装の品質保証に転用しません。

## 1. 品質評価の層を分ける

| 層 | 検証対象 | 状態 |
| --- | --- | --- |
| L0 | C# ビルド・静的チェック・既存単体テスト | 既存の build CI |
| L1 | 正解ラベル付きの固定コーパス（日英判定・辞書・候補） | 既存の Quality.Run を新レポートに統合 |
| L2 | OS 非依存の逐次入力・編集の整合性・例外・ランダム入力・再現性・正解付きhost再生 | 今回の新規QA |
| L3 | 境界Precision/Recall/F1、候補Recall、英語誤判定率、確定タイミング | 未実装（別途ゴールドデータを作成） |
| L4-Linux | 実IBus/Mozc + fcitx5デーモンを介した統合入力 | `linux.yml` のReady PRゲートとして実行（結果は同じPR HEADで確認） |
| L4-Windows | 実Windows TSF/IME・アプリ互換・入力消失・本家との共存、インストール/更新/アンインストール | 手動実機E2E未実施（専用VM/実機が必要） |

**L2 の合格は日英境界の正答率を示しません。**

## 2. CIの実行頻度と規模

- Pull Request と main へのコード更新: Ubuntu / Windowsで正解付きQuality.Run + 13分類の固定入力 + 1,200件の決定的ランダム入力を実行。
- 日次または手動実行: 同じテストに15,000件の決定的ランダム入力を追加。
- 固定oracle 11シナリオでは、最終host文書・全commit文字列列・未確定/確定後の表示・予期しないExpect拒否を正解と比較する。日本語・英語・日英混在・略語・URL・メール・Backspace・passthroughを含む。すべての入力がこの11件と同様に正解付けされたわけではない。
- PRの単語ごとの品質後退は既存 `pr-checks` が検出する。新QAだけでベースライン比較が完了したとは扱わない。
- 先頭の固定入力と最大250件のランダム入力を独立セッションで再実行し、出力/編集イベント列の完全一致を要求。
- 入力セッションのJSONシリアライズは代表フレームについて検証。
- C#品質テストの合格基準は既存互換の **全体95%以上、各カテゴリ80%以上**。今後引き上げる前に現状を測定する。

## 3. 何を「件数」と数えるか

保存する `meltia/quality-gate-v1` JSON には少なくとも以下を含む。

- `oracleCases`: 人が期待する文書とcommit列を固定した11シナリオの件数（`safetyCases`と独立）。
- `safetyCases`: 固定入力+ランダム入力を実行した件数（再実行は別の欄で数える）。
- `uniqueInputs`: 上記のうち重複を除いた実入力文字列数。
- `processedFrames`: シミュレートした打鍵フレーム数。
- `determinismRuns`: 独立セッション再実行検証の回数。
- `goldCases`: 既存の固定正解付き品質テストの件数。
- `manuallyReviewedNewGoldCases`: 新QAで人が確認して正解ラベルを付けた件数（**現時点0**）。
- `gold.categories`: カテゴリ別正解数/総数。
- `latency.p50/p95/p99`: 一入力あたりの時間（観測用、合否基準外）。

生成した15,000件を「15,000件の正答を確認した」と表現してはならない。`oracleCases`も通常の正解付き単語分割・境界ラベル数ではなくhost再生契約シナリオ数である。

## 4. ゲート

**失敗でブロック:** 処理例外、負の削除文字数、シミュレートしたテキストより長い不適切な削除、ホスト側Expectガード拒否、oracleのhost文書/commit列/未確定表示の不一致、確定済み入力の再生が非決定的、JSONシリアライズ異常、品質既存閾値違反。

**既存ゲートでブロック:** Windows/Linux/macOS Build/Test、PR前後での正解ケース後退（`pr-checks`）。Ready for review のPRでLinuxネイティブの対象ファイルに変更がある場合は `linux.yml` のIBus/Mozc実統合とfcitx5・NativeAOTテストも最新HEADで確認する。

**観測のみ:** CIランナーのp50/p95/p99。共有CIランナーでは変動が大きいので、同一環境での対照比較ができるまで性能しきい値は固定しない。

**未実施:** 日本語/英語の正解境界のP/R/F1、候補生成率、実Mozcの文節分割、Windows IMEエンドツーエンド、リリース品質基準。これらの実装結果と研究結果は別レポートにする。

## 5. 研究としての追加基準

次の段階でIncrementalBoundaryLab相当の検証を移植する。

1. 日英混在・固有名詞・略語・URL・メール・変則ローマ字など13分類のゴールドラベル付き入力。
2. 境界P/R/F1・完全一致・語種別誤り・未確定中の誤確定率・Backspace後の可逆性。
3. 生成した大量ケースの検証では構造不変条件と確定遷移を独立評価し、曖昧な入力を根拠なく正解扱いしない。
4. 辞書/変換器の版を固定し、候補探索/C#デコーダ/逐次入力/Windows TSFを別々に評価する。
5. 各実験にSHA、OS、seed、辞書版、総件数、固有入力数、人手確認件数、実行条件、失敗例、未検証項目を残す。

## 6. 再現方法

PR向け:
~~~bash
dotnet run --project src/Meltype.Core.Tests -c Release -- --meltia-qa out/qa.json 1200
~~~

大規模試験:
~~~bash
dotnet run --project src/Meltype.Core.Tests -c Release -- --meltia-qa out/qa.json 15000
~~~

GitHub Actions `meltia-strict-quality` のArtifactsにOSごとのJSONを30日間保持する。依存辞書が更新された場合は、以前の結果と直接比較せず条件差を記録する。

**注意:** `--meltia-qa` 自体はOSに依存しないシミュレーションです。Linux実IBus/Mozc E2Eは別ワークフローに分離し、Windowsの実入力/TSF/IME登録・更新・署名・本家Meltypeとの同居安全性は未確認です。実機の記録方法は [OS別E2E確認手順](MELTIA_OS_E2E_VERIFICATION.md) を参照してください。
