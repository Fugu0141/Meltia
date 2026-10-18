# Meltia

**Meltia** は、[Meltype](https://github.com/yksr-melt/Meltype) を基に開発する、非公式・独立運営の日本語入力ソフトウェアプロジェクトです。日本語・英語が混在する入力の自然さと、IME の安定性の向上を目指します。

> **現在は独立WindowsアプリのPreviewを実装中です。** `src/Meltia.Windows`にMeltypeコード再利用の実行ファイル構成を追加しましたが、GitHub ActionsのWindowsビルド・無フックセルフテストは成功していますが、実キー入力のWindows E2Eは未検証で、正式なTSF IME登録と安定配布版はありません。元のMeltype用インストーラーをMeltiaの名前で実行しないでください。

## 開発目標

- **安定性** — 再現可能な不具合報告、回帰テスト、CI に基づく改善
- **自然な日英混在入力** — 日本語と英語の境界を逐次推定し、誤変換を減らす
- **IME としての完成度** — 入力・確定・取り消し・学習・辞書との連携を検証
- **検証可能な改善** — 精度だけでなく誤判定の種類、遅延、安定性も記録

境界判定方式は [IncrementalBoundaryLab](https://github.com/Fugu0141/IncrementalBoundaryLab) で研究中です。独立したMeltia Coreでは、v0.5の基盤に続き、**[v1.0-devの不確実性レビュー](docs/MELTIA_V10_UNCERTAINTY_REVIEW_20261012.md)を研究用オプションとして実装**（v0.7候補生成・v0.8音韻ラティス・v0.9経路評価は維持）しています。[Meltypeとの比較アダプター](docs/MELTIA_SHADOW_COMPOSITION_BRIDGE.md)を基に、**独立WindowsキーボードPreviewの表示判定にMeltia Coreを任意ONで接続**しました。Windows TSFとしてのMeltia専用登録、実入力での精度・安定性はまだ未検証です。

## 現在の状態と今後

| 項目 | 状況 |
| --- | --- |
| Meltype からのソース・Git 履歴の継承 | 完了 |
| Meltia 用の説明・ライセンス方針 | 整備中 |
| アプリ名・IME 登録・インストール先・自動更新の分離 | Windowsプレビューの実行名・設定・起動時登録・更新停止は分離。正式TSF・インストーラーは未実装 |
| Meltia 専用の安定版リリース | 未実施 |
| 新しい境界判定アルゴリズムの統合 | WindowsキーボードPreviewに任意ONの表示判定フックを実装。実機打鍵試験・正式TSF統合は未完了 |

詳細は [開発ロードマップ](docs/MELTIA_ROADMAP.md) を参照してください。

## ドキュメント

- [プロジェクトの由来と本家との関係](docs/MELTIA_ORIGIN.md)
- [ライセンスと著作権の取り扱い](docs/MELTIA_LICENSE_POLICY.md)
- [開発ロードマップ](docs/MELTIA_ROADMAP.md)
- [Meltype上流変更の受け入れとMeltia段階的リファクタリング方針（レビュー用）](docs/MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md)
- [Meltype上流変更の受け入れ判定シート（複製用）](docs/MELTIA_UPSTREAM_INTAKE_TEMPLATE.md)
- [Meltype v1.2.1差分78ファイルの三方比較・R0棚卸し台帳](docs/MELTIA_MELTYPE_V121_R0_FILE_INVENTORY_20261017.md)
- [Meltype v1.2.1までの145コミット全件・参照番号索引](docs/MELTIA_MELTYPE_V121_R0_COMMIT_INDEX_20261017.md)
- [Meltia TSF/IME化の独立性・実機ゲート・バージョン更新要件](docs/MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)
- [H11 Phase 0-A3：独立自然文holdoutの収集・封印・評価手順](docs/MELTIA_H11_PHASE0_A3_HOLDOUT_POLICY_20261017.md)
- [H10 境界判定v2 要件定義（Meltype Issue駆動・研究フェーズ）](docs/MELTIA_H10_BOUNDARY_V2_REQUIREMENTS_20261014.md)
- [H10 境界判定v2 試作実装・47件の新旧診断・再現手順](docs/MELTIA_H10_PROTOTYPE_COMPARISON_20261014.md)
- [H10追加調査：Meltype Open/Closedの40 Issue・56入力の境界評価（全件結果）](docs/MELTIA_H10_MELTYPE_ISSUES_EXPANDED_20261014.md)
- [H11語・音韻同時分割の修正計画（103ケースの失敗分析）](docs/MELTIA_H11_JOINT_SEGMENTATION_PLAN_20261014.md)
- [H11の実装・103ケースの新旧実測・未解決31件・処理速度](docs/MELTIA_H11_RESEARCH_RESULTS_20261014.md)
- [H11統合後の課題一覧・改修優先順位・次期開発の実装手順と受け入れ条件](docs/MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md)
- [H11 Phase 0-A1：103ケースの不変ベースライン・再現性ゲート](docs/MELTIA_H11_PHASE0_BASELINE_20261017.md)
- [H11 Phase 0-A2：合成長文・編集操作の性能計測器と再現手順](docs/MELTIA_H11_PHASE0_A2_BENCHMARK_20261017.md)
- [H11 Phase 1：スコア差分・探索脱落理由を可視化するDiagnosticTraceの要件・基本設計](docs/MELTIA_H11_PHASE1_DIAGNOSTIC_TRACE_20261017.md)
- [H11 Phase 2 / PR-C1：UTF-16・保護区間・明示ロックの互換契約と共通化](docs/MELTIA_H11_PHASE2_SAFE_SPANS_20261017.md)
- [H11 Phase 2 / PR-C2：H10の重複ポリシーとUTF-16/ロック互換性の段階的共通化](docs/MELTIA_H11_PHASE2_C2_H10_SAFETY_20261017.md)
- [H11 Phase 2 / PR-C3：従来CoreのUTF-16・URL/メール・ロック互換共通化](docs/MELTIA_H11_PHASE2_C3_LEGACY_SAFETY_20261017.md)
- [H11 Phase 3 / PR-D1a：ローマ字判定のSpan化と割り当て削減の要件・検証](docs/MELTIA_H11_PHASE3_D1A_ROMAJI_SPAN_20261017.md)
- [H11 Phase 3 / PR-D1b：文字尤度のSpan化とモデル完全互換の検証](docs/MELTIA_H11_PHASE3_D1B_CHARACTER_EVIDENCE_20261017.md)
- [H11 Phase 3 / PR-D2：ビーム保持最適化の要件・同値テスト・測定](docs/MELTIA_H11_PHASE3_D2_BEAM_SELECTION_20261017.md)
- [H11 Phase 4 / PR-E1：共通前向きDecoderの設計・オプトイン互換実装・比較ゲート](docs/MELTIA_H11_PHASE4_E1_FORWARD_DECODER_20261017.md)
- [H11 Phase 4 / PR-E2：候補生成器・Top-K経路射影の分離と互換テスト](docs/MELTIA_H11_PHASE4_E2_GENERATOR_PROJECTION_20261017.md)
- [H11 Phase 4 / PR-E3：H10前向き探索の共通Decoderアダプターと互換テスト](docs/MELTIA_H11_PHASE4_E3_H10_FORWARD_ADAPTER_20261017.md)
- [H11 Phase 4 / PR-E4：v0.8系後向き探索と多様性保持を分離する設計・実験](docs/MELTIA_H11_PHASE4_E4_BACKWARD_DECODER_20261017.md)
- [H11 Phase 4 / PR-E5：従来Core後ろ向きTop-Kの共通Decoder互換実験](docs/MELTIA_H11_PHASE4_E5_LEGACY_BACKWARD_20261017.md)
- [開発バージョン管理（v0.5〜v1.0-dev）](docs/MELTIA_VERSIONING.md)
- [v0.7 未知英語候補生成の実験・検証](docs/MELTIA_V07_UNKNOWN_LATIN_20261012.md)
- [v0.8 音韻ラティス・経路探索の実装と実測](docs/MELTIA_V08_PHONETIC_LATTICE_20261012.md)
- [v0.9 複数英語区間の文脈的評価と追加290件の比較](docs/MELTIA_V09_CONTEXTUAL_ISLANDS_20261012.md)
- [v1.0-dev 境界の不確実性レビューと867件評価](docs/MELTIA_V10_UNCERTAINTY_REVIEW_20261012.md)
- [v1.0 フェーズ2：96文字を超える日英混在入力の候補生成研究](docs/MELTIA_V10_LONG_RUN_PHASE2_20261012.md)
- [v1.0 フェーズ3：候補上限128件での位置的多様性と1019件評価](docs/MELTIA_V10_DIVERSE_CANDIDATES_PHASE3_20261012.md)
- [v1.0 フェーズ4：日本語吸収を抑制するTop1経路スコアリング・1067件評価](docs/MELTIA_V10_FLANK_RANKING_PHASE4_20261012.md)
- [Meltia Windows Preview — 独立アプリのビルド・起動・安全性・残課題](docs/MELTIA_WINDOWS_PREVIEW.md)
- [T1-A：Keyboard Previewの本家TSF・更新経路への誤接続防止ガード](docs/MELTIA_T1_KEYBOARD_PREVIEW_ISOLATION_GUARD_20261017.md)
- [R1：Meltype継承Compositionの入力・確定・副作用契約とcharacterization tests](docs/MELTIA_R1_COMPOSITION_BEHAVIOR_CONTRACT_20261017.md)
- [T1-B：継承TIPのC#／C++ IPC通信契約と独立IME移行ゲート](docs/MELTIA_T1B_TIP_IPC_PROTOCOL_CONTRACT_20261017.md)
- [R1b：CaptureGate・キー再送・確定副作用の順序契約](docs/MELTIA_R1B_INPUT_CAPTURE_ORDER_CONTRACT_20261017.md)
- [T2：Meltia専用TSF識別子・独立登録の設計と無登録ガード](docs/MELTIA_T2_TSF_IDENTITY_AND_REGISTRATION_BOUNDARY_20261017.md)
- [R1c：UTF-16原文保持・保護区間・編集操作の回帰契約](docs/MELTIA_R1C_UTF16_RAW_PROTECTION_CONTRACT_20261017.md)
- [T3a：Meltia Native TIPの無登録・隔離x64/x86ビルド手順と検証](docs/MELTIA_T3A_ISOLATED_NATIVE_TIP_BUILD_20261017.md)
- [R2/T3b：Meltia専用C#↔Native v1 IPCゲート（無登録・レビュー用）](docs/MELTIA_R2_T3B_ISOLATED_IPC_20261018.md)
- [2026-10-13 Windows実機入力フィードバック・改修の検証計画](docs/MELTIA_WINDOWS_FIELD_TEST_20261013.md)
- [Issue #32: `changesuru` の英日境界修正・再現テストと実機確認手順](docs/MELTIA_ISSUE_32_CHANGE_SURU_20261014.md)
- [2026-10-13 Issue / ブランチ整理（完了Issue・削除候補・安全な削除手順）](docs/MELTIA_ISSUE_BRANCH_CLEANUP_20261013.md)
- [貢献方法](CONTRIBUTING.md)
- [移動した原著コードの著作権を検証する notices bot の方針（Issue #22）](docs/MELTIA_ISSUE_22_NOTICES_PROVENANCE_20261014.md)
- [Issue #23: 過去タグのLinux公開ワークフロー隔離と管理者確認手順](docs/MELTIA_ISSUE23_LEGACY_LINUX_RELEASE_GUARD.md)
- [セキュリティ報告](SECURITY.md)
- [サードパーティー通知](THIRD-PARTY-NOTICES.md)
- [開発・ビルド手順（本家から継承、Meltia 用に未更新）](docs/DEVELOPMENT.md)

## Meltia Windows Keyboard Preview（実機未検証）

Meltypeのソースを土台に、変換ボックス・辞書・漢字変換・キー入力処理を
再利用する独立実行ファイルの構成を追加しました。
Windows + .NET SDK 10環境で手元でビルドできます。

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-meltia-windows-preview.ps1
```

MeltiaのネイティブTSF登録は行わず、最初は**キーボード型プレビュー**として
動作させます。GitHub Actionsに依存しないスモークテストと、
本家Meltypeのデータや自動更新を変更しない設計が含まれます。
**GitHub ActionsでWindowsビルド・無フックセルフテストは成功していますが、修正後の実際のキー入力は未確認です。**
[手順と残課題はこちら](docs/MELTIA_WINDOWS_PREVIEW.md)。

## 開発者向け

現時点のソリューション名は **`Meltype.sln`** のままです。開発環境が整っていれば、例えば次のコマンドで既存コードのビルドを試せます。

```powershell
dotnet build Meltype.sln -c Release
```

Windows の IME コンポーネントや配布パッケージのビルドには別途ツールが必要です。詳細は継承した [開発文書](docs/DEVELOPMENT.md) を確認してください。既存の `docs/USAGE.md`・`docs/RELEASE.md`・スクリプトは **本家 Meltype 向けの記述を含む** ため、Meltia の利用・配布手順としてそのまま用いないでください。

## 原著作権・ライセンス

本プロジェクトは [yksr-melt/Meltype](https://github.com/yksr-melt/Meltype) の派生版で、**本家の公式版・後継版ではありません**。本家の作者・貢献者による承認や保証を意味しません。元の開発者、貢献者、ロゴ制作者などへのクレジットと履歴は [本家リポジトリ](https://github.com/yksr-melt/Meltype) および Git 履歴から確認できます。

プログラム本体は [GNU GPL v3 またはそれ以降](LICENSE)（`GPL-3.0-or-later`）に従います。MIT への一括変更は行っていません。辞書や同梱コンポーネントには別のライセンスがあるため、[ライセンス方針](docs/MELTIA_LICENSE_POLICY.md) と [サードパーティー通知](THIRD-PARTY-NOTICES.md) をあわせて確認してください。

Meltype のロゴ・名称を Meltia の独自ブランドや公式承認の表示として流用する予定はありません。現在残る本家の画像等は継承した資料として扱い、対外的な配布前に権利と利用条件を確認します。
