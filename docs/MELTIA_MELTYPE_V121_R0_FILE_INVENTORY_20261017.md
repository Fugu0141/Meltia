# R0 — Meltype v1.2.1までの変更棚卸しとMeltia受け入れ候補（2026-10-17）

> **台帳初版。実装未着手・各ファイルの採用承認ではない。** 全145コミットのメッセージ・参照番号は [R0コミット索引](MELTIA_MELTYPE_V121_R0_COMMIT_INDEX_20261017.md) に別途記録。 [Meltype受け入れ方針](MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md) のR0を進める。上流の**変更ファイルを漏れなく**列挙した自動比較と、リスクによる**暫定仕分け**を記録する。各コミットの移植適否、三方の意味差、テストの期待値は次のレビューで確定させる。

## 1. 比較点と作業対象

- B＝Meltype/Meltia分岐起点 `cc456a00521ae6143680196adf0f0c4b96d131ae`（Git履歴上の共通祖先）。
- U＝Meltype v1.2.1 `47ace1428e5d4c9213f64dbbaf00d73a01386c69`（**固定された最初の上流比較ターゲット**）。
- M＝Meltia main `55d5ad755d0a0976522ffe7d36154c63f285b268`（PR #51マージ後。移植はしない）。
- [B→U](https://github.com/yksr-melt/Meltype/compare/cc456a00521ae6143680196adf0f0c4b96d131ae...47ace1428e5d4c9213f64dbbaf00d73a01386c69)：**145コミット、78変更ファイル**。
- [B→M](https://github.com/Fugu0141/Meltia/compare/cc456a00521ae6143680196adf0f0c4b96d131ae...55d5ad755d0a0976522ffe7d36154c63f285b268)：**572コミット、187変更ファイル**。
- **同じファイルの変更が両側にあるものは 12 件**。競合とは「同パス変更」のみを指し、Gitの自動マージ失敗数・ロジック矛盾数ではない。上流で変更された別ファイルに依存するMeltia実装もあるため、非重複の66件も無審査移植しない。

`git diff B..U` は「upstreamの変更」であり「Meltiaに未実装の仕様」を意味しない。Meltiaが別ファイルに同等機能を持つか、移植元コミットが共通祖先以降に別方法で適用済みかは別途確認する。下表の「未変更*」も**安全にコピーできる意味ではない**。

## 2. 責務別の全件概要

| 領域 | B→Uファイル | 同パスでB→Mにも変更あり |
| --- | ---: | ---: |
| 配布・識別/CI | 13 | 3 |
| 文書 | 5 | 2 |
| 辞書データ | 4 | 1 |
| OS別 | 8 | 0 |
| Windows TSF/IME | 5 | 0 |
| 回帰テスト | 10 | 0 |
| Composition | 14 | 3 |
| Config/Core | 6 | 1 |
| その他 | 1 | 0 |
| Windowsホスト | 12 | 2 |
| **合計** | **78** | **12** |

**コード移植に先立つ順序**：まず上流の変更に付随する回帰テスト群を移植ではなく読解して、現行Meltiaで再現する契約を固定→入力・確定の既存動作を固定→次期バージョン要件に沿って候補を選択→純粋リファクタリングと変更採用は別PRとする。

## 3. ファイル別棚卸し（全78件、採否は未決）

「差分行数」はGitHub compareが報告する当該ファイルの追加+削除行数。「Meltia側同パス未変更」はB→Mで変更ファイルに挙がらないというだけで、間接依存の互換性は調査していない。

| upstreamパス | B→U | 差分行 | B→Mの同一パス | 暫定判断（調査先） |
| --- | --- | ---: | --- | --- |
| `.github/bot/pr-checks.mjs` | 変更 | 4 | Meltia側同パス未変更* | 隔離（F） |
| `.github/workflows/winget.yml` | 追加 | 47 | Meltia側同パス未変更* | 隔離（F） |
| `CONTRIBUTING.md` | 変更 | 1 | 要三方比較（Meltia側179行差分） | 本家手順混入回避 |
| `README.md` | 変更 | 11 | 要三方比較（Meltia側373行差分） | 本家手順混入回避 |
| `Uninstall-Meltype.ps1` | 変更 | 2 | Meltia側同パス未変更* | 隔離（F） |
| `dictionaries/candidates.txt` | 変更 | 19 | Meltia側同パス未変更* | 出典・精度評価（E） |
| `dictionaries/english.txt` | 変更 | 1 | Meltia側同パス未変更* | 出典・精度評価（E） |
| `dictionaries/phrases.txt` | 変更 | 4 | Meltia側同パス未変更* | 出典・精度評価（E） |
| `dictionaries/propernouns.txt` | 変更 | 4 | 要三方比較（Meltia側3行差分） | 出典・精度評価（E） |
| `docs/RELEASE.md` | 変更 | 4 | Meltia側同パス未変更* | 本家手順混入回避 |
| `docs/TSF_DESIGN.md` | 変更 | 18 | Meltia側同パス未変更* | 本家手順混入回避 |
| `docs/USAGE.md` | 変更 | 58 | Meltia側同パス未変更* | 本家手順混入回避 |
| `linux/TESTER-README.txt` | 変更 | 2 | Meltia側同パス未変更* | OS別保留 |
| `linux/fcitx5/CMakeLists.txt` | 変更 | 8 | Meltia側同パス未変更* | OS別保留 |
| `linux/fcitx5/meltype.cpp` | 変更 | 17 | Meltia側同パス未変更* | OS別保留 |
| `linux/ibus-engine-meltype` | 変更 | 15 | Meltia側同パス未変更* | OS別保留 |
| `linux/meltype.xml` | 変更 | 2 | Meltia側同パス未変更* | OS別保留 |
| `mac/README.md` | 変更 | 2 | Meltia側同パス未変更* | OS別保留 |
| `mac/Resources/Info.plist` | 変更 | 4 | Meltia側同パス未変更* | OS別保留 |
| `native/tip/LangBar.cpp` | 変更 | 2 | Meltia側同パス未変更* | 独立設計（F） |
| `native/tip/TextService.cpp` | 変更 | 140 | Meltia側同パス未変更* | 独立設計（F） |
| `native/tip/TextService.h` | 変更 | 23 | Meltia側同パス未変更* | 独立設計（F） |
| `packaging/Meltype.iss` | 変更 | 6 | Meltia側同パス未変更* | 隔離（F） |
| `packaging/meltype-ime.ps1` | 変更 | 11 | Meltia側同パス未変更* | 隔離（F） |
| `packaging/setup-ime.ps1` | 変更 | 9 | Meltia側同パス未変更* | 隔離（F） |
| `packaging/uninstall.ps1` | 変更 | 3 | Meltia側同パス未変更* | 隔離（F） |
| `src/Meltype.Core.Tests/CompositionTests.cs` | 変更 | 475 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/CorrectionTests.cs` | 追加 | 62 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/FeedbackTests.cs` | 変更 | 260 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/MemoryPersistenceTests.cs` | 変更 | 49 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/ProfileTests.cs` | 変更 | 2 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/SessionFacadeTests.cs` | 変更 | 141 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/SettingsJsonTests.cs` | 変更 | 11 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/SnippetTests.cs` | 追加 | 120 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core.Tests/WordJudgeTests.cs` | 追加 | 31 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype.Core/Composition/CompositionController.cs` | 変更 | 360 | 要三方比較（Meltia側738行差分） | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/CompositionDetector.cs` | 変更 | 138 | 要三方比較（Meltia側107行差分） | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/CompositionText.cs` | 変更 | 7 | 要三方比較（Meltia側25行差分） | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/ConversionPreferences.cs` | 変更 | 11 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/Corrections.cs` | 追加 | 60 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/LanguageMemory.cs` | 変更 | 8 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/MeltypeSession.cs` | 変更 | 99 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/Predictor.cs` | 変更 | 10 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/ScoredSegmentation.cs` | 変更 | 3 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/SigilWord.cs` | 変更 | 3 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/Snippets.cs` | 追加 | 247 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/TranslationDictionary.cs` | 変更 | 7 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/UserDictionary.cs` | 変更 | 7 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Composition/WordJudge.cs` | 追加 | 63 | Meltia側同パス未変更* | 契約固定→設計（C/D） |
| `src/Meltype.Core/Config/AppPaths.cs` | 変更 | 1 | Meltia側同パス未変更* | 互換確認（B/C） |
| `src/Meltype.Core/Config/Backup.cs` | 変更 | 2 | Meltia側同パス未変更* | 互換確認（B/C） |
| `src/Meltype.Core/Config/CoreJsonContext.cs` | 変更 | 5 | Meltia側同パス未変更* | 互換確認（B/C） |
| `src/Meltype.Core/Config/ProjectInfo.cs` | 変更 | 14 | Meltia側同パス未変更* | 互換確認（B/C） |
| `src/Meltype.Core/Config/Settings.cs` | 変更 | 59 | Meltia側同パス未変更* | 互換確認（B/C） |
| `src/Meltype.Core/Input/KeyEvent.cs` | 変更 | 2 | Meltia側同パス未変更* | 個別判断 |
| `src/Meltype.Core/Meltype.Core.csproj` | 変更 | 2 | 要三方比較（Meltia側1行差分） | 互換確認（B/C） |
| `src/Meltype.Mac.Native/Exports.cs` | 変更 | 19 | Meltia側同パス未変更* | OS別保留 |
| `src/Meltype.Tests/WindowsTests.cs` | 変更 | 97 | Meltia側同パス未変更* | 契約調査 |
| `src/Meltype/AppInfo.cs` | 変更 | 4 | 要三方比較（Meltia側31行差分） | 隔離（F） |
| `src/Meltype/Composition/CompositionService.cs` | 変更 | 61 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/Composition/FocusInspector.cs` | 変更 | 144 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/Composition/KanjiConverter.cs` | 変更 | 53 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/Composition/ModeIndicatorWindow.cs` | 変更 | 19 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/Composition/UiAutomation.cs` | 変更 | 45 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/IME/Imm32ImeController.cs` | 変更 | 30 | Meltia側同パス未変更* | 独立設計（F） |
| `src/Meltype/Input/RemoteControl.cs` | 追加 | 58 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/Meltype.csproj` | 変更 | 2 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/MeltypeEngine.cs` | 変更 | 165 | 要三方比較（Meltia側6行差分） | 副作用監査（C/D） |
| `src/Meltype/Program.cs` | 変更 | 51 | 要三方比較（Meltia側29行差分） | 隔離（F） |
| `src/Meltype/Startup.cs` | 変更 | 136 | 要三方比較（Meltia側8行差分） | 隔離（F） |
| `src/Meltype/Tip/TipServer.cs` | 変更 | 79 | Meltia側同パス未変更* | 独立設計（F） |
| `src/Meltype/UI/CorrectionReportDialog.cs` | 追加 | 127 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/UI/SnippetsForm.cs` | 追加 | 172 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/UI/TrayApplicationContext.cs` | 変更 | 148 | 要三方比較（Meltia側78行差分） | 副作用監査（C/D） |
| `src/Meltype/UI/UpdateChangesDialog.cs` | 追加 | 92 | Meltia側同パス未変更* | 副作用監査（C/D） |
| `src/Meltype/Updater.cs` | 変更 | 50 | Meltia側同パス未変更* | 隔離（F） |
| `tools/Test-SetupIme.ps1` | 変更 | 4 | Meltia側同パス未変更* | 隔離（F） |
| `tools/make-manifests.mjs` | 変更 | 33 | Meltia側同パス未変更* | 隔離（F） |

## 4. リスクによるトリアージ（着手はR1以降）

### A. **先に契約試験として分析するもの**

- `src/Meltype.Core.Tests/CompositionTests.cs`、`FeedbackTests.cs`、`SessionFacadeTests.cs`、`MemoryPersistenceTests.cs` と新規 `CorrectionTests.cs`、`SnippetTests.cs`、`WordJudgeTests.cs`：メソッド名と期待値を比較し、Meltiaにも意味があるケースをテスト候補へ。**上流コードをまだ取り込まない**。
- `src/Meltype.Core/Composition/CompositionController.cs` と `CompositionDetector.cs`、`CompositionText.cs`：双方で同パスが変更されている。段階的にイベント・区間・確定の副作用と原文維持を固定してから設計する。Meltiaの `CompositionDetector.BoundaryRefinement` を失わない。
- `MeltypeSession.cs`、`Corrections.cs`、`Snippets.cs`、`WordJudge.cs`：追加機能を丸ごと移植する前に、既存Meltiaの状態・補正/学習/変換契約との意味差を確認する。新機能は次期要件定義まで採用未定。

### B. **本家のまま移すと危険な領域**

- `native/tip/TextService.cpp`／`TextService.h`／`LangBar.cpp`、`src/Meltype/Tip/TipServer.cs`：TSFのCOM登録とC#パイプ間のABI／プロトコルを包括的に評価。**正式Meltia IME用GUID、PIPE、登録／削除／認可は別設計が必要**。ネイティブソースが存在しても現行Previewで機能有効ではない。
- `src/Meltype/Program.cs`、`Startup.cs`、`TrayApplicationContext.cs`、`MeltypeEngine.cs` はMeltia側でも変更あり。特にPreviewの独立データ先、既定Keyboardモード、二重起動対策、更新停止を維持する。
- `packaging/*`、`tools/make-manifests.mjs`、`.github/workflows/winget.yml`、`src/Meltype/Updater.cs`、`Uninstall-Meltype.ps1`：本家の名前／登録／配布先／Release workflowをMeltiaに直接取り込まない。
- `dictionaries/*`：継承辞書の内容変更が単なる改善とは限らず、既知の `commit` / `meltiawotamesu` などを含む英日混在評価・出典/許諾を別PRで行う。

### C. **依存を計測して小さく進められるもの**

- `Config`領域や軽量な単一ファイル変更は各ファイルの依存関係・挙動差を確認した後、内部同値テスト付きで候補化。特に `ProjectInfo`・`AppPaths`は識別子／設定保存先に関係するため、見た目の差分が小さくても安易に適用しない。
- Linux/macOSの変更はWindowsのCI合格を根拠に「移植済み」としない。OS別の受け入れ計画へ分類する。

## 5. R0の未完了項目と引継ぎ

**この初版で完了したもの**：SHA固定、B→U/B→Mの比較、78ファイル全行の名前・変更量・同パス変更検出、責務分類、現行Meltiaを壊す可能性のある対象の隔離。

**まだ完了していないもの**：145コミットの履歴は索引化済みだが元PR/Issueとの正確な対応づけ・内容レビュー、同等実装のコード意味比較、入力サンプル別の挙動比較、各変更のA〜G確定・依存PR・ライセンス原本調査、実Windowsテスト。行の暫定判断を「採用」と解釈しない。

後続の選別作業では [判定シート](MELTIA_UPSTREAM_INTAKE_TEMPLATE.md) を対象ごとに複製し、`covered` / `direct` / `adapt` / `redesign` / `deferred` / `rejected` の**確定判定は根拠が揃ってから**付ける。自然文holdoutや本番デフォルトを変えず、個別PRの回帰テスト・レビューを条件にする。

## 再現コマンド

```bash
git remote add upstream https://github.com/yksr-melt/Meltype.git  # 初回のみ
git fetch upstream --tags
git diff --name-status cc456a00521ae6143680196adf0f0c4b96d131ae 47ace1428e5d4c9213f64dbbaf00d73a01386c69
git diff --stat cc456a00521ae6143680196adf0f0c4b96d131ae 47ace1428e5d4c9213f64dbbaf00d73a01386c69
git diff --name-status cc456a00521ae6143680196adf0f0c4b96d131ae 55d5ad755d0a0976522ffe7d36154c63f285b268
```

本台帳は確認時の固定SHAに対する結果。Meltia mainが進んだ場合はM側を更新し、上流の次バージョンは新しいUに差し替えず**別スナップショット**として記録する。
