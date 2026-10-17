# H11 Phase 2 / PR-C2 — H10安全処理の段階的共通化（2026-10-17）

> **本PRは研究用H10のみを変更する、出力互換のリファクタリング。** 前段：[Phase 2 / C1](MELTIA_H11_PHASE2_SAFE_SPANS_20261017.md)、[PR #40](https://github.com/Fugu0141/Meltia/pull/40)（main統合コミット `59968d85d25824625891436d54f60958588ca3f7`）。上位計画：[H11次期開発準備計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 2。

## 1. 要件 / 非対象

- `H10BoundaryPrototype`のUTF-16サロゲート境界、URL・メール自動保護区間検出、明示EN/JAロック矛盾判定のみをC1で追加済みの内部共通関数へ置換し、既存の **raw、候補（スパン/順序/由来/スコア）、全Top-K（順序/スコア）、NeedsConfirmation、AutoCommitAllowed、例外型・文言** を維持する。
- **H10特有の挙動を統一の名目で修正しない**。H11や従来Coreは今回変更しない。
- `MeltiaBoundaryEngine`の保護ポリシー移行は別PR-C3。H10/H11の推論、辞書、スコア、探索、Windows Preview/TSF、Release、既存ベンチの判定閾値は変更しない。
- 既知Issue 47＋56＝103例と追加の合成正負例で互換性確認。**H11凍結103件はH11専用の厳密スナップショット**であり、H10の完全スナップショットが得られたとは主張しない。H10については既存診断のケース別レポート・契約テスト・元コードとの意味的照合を併用する。
- 独立自然文holdout、実キーE2E、アルゴリズム精度改善は対象外。

## 2. 互換仕様表（実装前のコード照合）

| ケース | H10の現行仕様 | 移行方法 |
| --- | --- | --- |
| 自動URLとメール（URL内のメールを含む） | URL→メール順に一致範囲を収集。**完全同一だけ省き**、異なる区間の重複は後で`Conflicting mandatory spans` | `ProtectedSpanDetector.AppendMatches(raw, mandatory, SkipExactDuplicate, Url, Email)` |
| 手動ENとProtectedの同一区間 | `Distinct`で1件に整理。同じ自動URLなら重複追加なし | 同じ順序の既存`Distinct`を保持 |
| 手動EN/Protectedと自動URLが一部重複 | 自動URLを**捨てず**追加し、矛盾として拒否（H11との差異） | `SkipAnyOverlap`へ変更しない |
| URL全体と同一区間の手動EN/Protected | 完全同一なので1件、正常にmandatory候補になる（内部メールで別区間が重ならないURLの場合） | 同じ`SkipExactDuplicate` |
| ENとJA、異なるENとENの交差・包含 | `Conflicting mandatory spans`、同一区間のJA重複は許容 | `HasCrossOverlap` + `HasDistinctOverlap` |
| 異なるJA同士の交差・包含 | `Overlapping Japanese locks` | `HasDistinctOverlap` |
| 無効なUTF-16ロック（空/逆転/負・長さ超過） | `Invalid H10 span` | 元の**範囲チェックを先に保持**し、境界判定だけ共通化 |
| 高位・低位サロゲートの間で区間開始/終了 | `H10 span splits UTF-16 surrogate pair` | `BoundarySpanValidator.SplitsSurrogatePair` |
| `MeltypeEnglish` | H10の候補・ロックには使用しない | 入力を参照しないまま保持 |
| UTF-16が1024文字を超過 | `ArgumentOutOfRangeException` | 既存先行ガード維持 |

**重要**：入力の検証順も互換性の一部。元コードは **明示JA全件→mandatory全件→mandatory矛盾→JA同士矛盾** の順に検査していた。順序を入れ替えて異なる例外を出さない。H10内部の候補追加時の境界条件やrawコピーは触れない。

## 3. 基本設計と受け入れゲート

1. `H10BoundaryPrototype.cs`のRegex定義/オプションは変更せず、既存`mandatory`リストを`SkipExactDuplicate`の引数として渡す。`Validate`は数値の範囲検査をそのまま行い、サロゲート判定のみ内部共通メソッドへ委譲。例外文言を変更しない。
2. ロック矛盾判定を`BoundarySpanValidator.HasCrossOverlap(mandatory, explicitJapanese)` / `HasDistinctOverlap(mandatory)` / `HasDistinctOverlap(explicitJapanese)`へ移す。候補グラフ・DP・スコア計算への変更は行わない。
3. `Phase2H10SafetyContractTests.cs`に、URL内メール、同一ロック、手動と自動の部分重複・一致、EN/JA交差・包含、JA同士の衝突、UTF-16不正範囲と正常な絵文字、`MeltypeEnglish`の非使用、例外の**型/文言**を固定する。
4. **CI受け入れ**：Core契約・H10比較（47+56件）・H11凍結103件・Phase 1 DiagnosticTrace・long/edit smoke・Windows Preview無フック試験が成功。従来Coreのテストが変化した場合は中止。元のIssue診断数値は既知診断用であり自然文holdout精度とは区別。
5. 各PRを小さく保ち、レビュー完了までmainへのマージは行わない。検証結果、留保、ジョブへのリンクをこのMarkdownに追記する。

### 再現コマンド（.NET SDK 10 / Release）

```sh
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --output artifacts/h10/phase2-c2-47.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --input tests/Meltia.H10Evaluation/issue_intents_extra_20261014.jsonl --output artifacts/h10/phase2-c2-56.json --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
```

本ブランチの実測結果・Actions状態はPR作成後に追記する。評価したという主張は実際のCIを確認してから行う。
