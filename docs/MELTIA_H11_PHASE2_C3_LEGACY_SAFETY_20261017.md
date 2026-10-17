# H11 Phase 2 / PR-C3 — 従来CoreのUTF-16・URL/メール・ロック互換共通化（2026-10-17）

> **対象：`MeltiaBoundaryEngine`の安全処理だけ。** C1 [PR #40](https://github.com/Fugu0141/Meltia/pull/40)・C2 [PR #41](https://github.com/Fugu0141/Meltia/pull/41) の統合後main（`8ffca0484ec08f9c75804a52ed4e90944c879fc9`）から独立実施。上位計画：[H11次期開発準備](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 2。

## 要件・非変更範囲

- 従来Coreの`CheckSpan`のUTF-16境界チェック、URL→メール自動保護、手動EN/Protected対JA・EN矛盾だけを`BoundarySpanValidator` / `ProtectedSpanDetector`へ移す。
- **raw、`Candidates`全件と順序・Score・Origin・Mandatory、`Routes`の順序/Score、`Truncated`、`Review`、`AutoCommitAllowed`、例外型・文言・順序を変えない。**
- `MeltiaBoundaryEngine`の全オプション（v0.5〜v1.0-dev）に同じ安全契約を適用し、元の辞書・候補生成・ビーム・ランキング・学習/入力ログ・Windows Preview・TSF・Releaseには変更しない。
- H10の`SkipExactDuplicate`へ寄せない。**従来CoreはH11と同じ`SkipAnyOverlap`、H10は`SkipExactDuplicate`という差異を維持**する。
- CI結果と正負契約を記録してPRをレビューへ出す。レビュー完了まではmainへ統合しない。

## 現行の仕様表（実装前コード読取）

| 項目 | 従来Coreの仕様 | C3での実装方針 |
| --- | --- | --- |
| Regex定義 | URL `https?://[^\\s]+` / メール `(?<![\\w.@])[\\w.+-]+@[\\w.-]+\\.[A-Za-z]{2,}`、どちらも`Compiled`のみ | **Regex本体・オプションを維持**し、URL→メール順のまま共通`AppendMatches`へ引き渡す |
| URL内メール | URL範囲を先に保護、メールがその範囲と重なれば無視 | `SkipAnyOverlap` |
| 手動EN/ProtectedとURLの部分重複 | 手動ロック優先：URLは追加しない | 既存`hard`リストをそのまま共通関数へ渡す |
| 同一区間のEN+Protected | `Distinct()`で単一化 | 既存`Distinct()`と順序を維持 |
| EN対JA・異なるEN同士の重複 | `Overlapping or contradictory mandatory ranges` | `HasCrossOverlap(hard, japanese) || HasDistinctOverlap(hard)` |
| JA対JA（重複・交差・包含） | H10/H11と異なり、**JA同士の矛盾を直接拒否しない** | 新しい拒否判定を設けない |
| `MeltypeEnglish` | JA→hard→MeltypeEnglishの順でUTF-16検証、既定で弱い候補、実験フラグで再評価 | **入力順・検証・候補の扱いを不変** |
| 長さ・空・逆転・範囲外のロック | `Invalid UTF-16 range`、境界検査より先に適用 | 数値ガードを残し、`SplitsSurrogatePair`にのみ委譲 |
| ペア内境界 | `Range splits a surrogate pair` | 既存例外メッセージ維持 |
| raw長 > 1024 | `ArgumentOutOfRangeException` | 同じガードを先に実行 |

### 実装計画

1. `src/Meltia.Core/Boundary/MeltiaBoundaryEngine.cs`の従来の2重Regex列挙を、`ProtectedSpanDetector.AppendMatches(raw, hard, SkipAnyOverlap, ProtectedUrl, ProtectedEmail)`に置換。Regex生成・初期hardの順番を変えない。
2. `CheckSpan`内の数値ガードと例外文字列を残し、サロゲート検査を`BoundarySpanValidator.SplitsSurrogatePair`に置換。入力のバリデーション列挙順は変えない。
3. hardに対する旧`Any`の矛盾条件を共通`HasCrossOverlap`と`HasDistinctOverlap`へ置換。JA同士の条件は新設しない。`MeltypeEnglish`検証・順位の動作はそのまま。
4. `Phase2LegacySafetyContractTests`を増設。URL内メール、manual EN/Protectedの重複・包含・交差、JAロックの交差とENとの衝突、UTF-16境界（正負）と例外文字列、`MeltypeEnglish`（無効範囲拒否・通常候補・オプション）を確認。既存`Phase2SafetyContractTests`と`Phase2H10SafetyContractTests`は変更しない。
5. Core契約、既知Issue 47＋56 H10/H11比較・凍結103件指紋、全オプションのCore/Bridge回帰、Windows Preview無フック試験、strict-qualityを実施。リファクタリングによる精度/性能向上を主張しない。

### 受け入れと留保

- 同じ入力/同じオプションで候補とTop-Kが変わらないことを、既存契約・追加正負例・CI合格で確認する。独立holdoutの精度と本家Meltype Windows実キーE2Eは未検証。
- H11の103件は厳密に凍結される一方、従来Coreの`Candidates`/全Top-Kの**全103ケースの専用fingerprintは現段階で固定されていない**。集計値一致を完全な経路同一性証明とみなさない。今後はPhase 0のような旧Core/H10専用スナップショットを独立PRで整備する。
- レビューフィードバック、CI run、実際の一致/非一致は後からこの文書に追記する。

### 再現コマンド（.NET 10 SDK）

```sh
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --output artifacts/h10/phase2-c3-47.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --input tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl --output artifacts/h10/phase2-c3-56.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
```
