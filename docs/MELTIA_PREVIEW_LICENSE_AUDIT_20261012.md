# Meltia v1.0.0-beta.1 — 配布ライセンス監査（2026-10-12）

## 対象

`release/v1.0.0-beta.1-preview` ブランチにおける **Windows x64・framework-dependent・Keyboard PreviewのZIPのみ**。リポジトリ全体や自己完結型Windows配布、Mac/Linux/Mozcを含むパッケージの包括的な権利保証ではない。

## 確認済み

| 対象 | 結論・配布条件 |
| --- | --- |
| Meltype由来C#コード／Meltiaと連結した実行ファイル | GPL-3.0-or-laterを維持。ルート`LICENSE`をZIPに含め、原著作権表示・Git履歴は保持 |
| Meltiaの新コード | GPL-3.0-or-laterの派生リポジトリ内に配置。元の著作権・寄与者の表示を保持 |
| JMdict由来データ（`translations`・`readings`・`loanwords`） | CC BY-SA 4.0。辞書の出典・ライセンスが`THIRD-PARTY-NOTICES.md`に掲載されていることを確認 |
| Wiktionary由来の`meanings` | CC BY-SA 4.0。データ由来とライセンスが通知に記録されていることを確認 |
| SCOWL・Unicode CLDR由来の語彙・絵文字 | 権利表示とライセンスを`THIRD-PARTY-NOTICES.md`に継承。実際に含まれる辞書の記載を引き続き維持 |
| .NETランタイム | **framework-dependent限定**。ランタイムのバイナリを同梱せず、利用者に.NET 10 Windows Desktop Runtimeを別途要求 |
| Meltypeのロゴ・独自画像・本家インストーラー・TSFネイティブDLL | 権利/識別子が別のため、**Preview ZIPに含めない**。GitHubの自動生成ソースアーカイブには継承資料が残りうる点は別途監査対象 |

## CIで強制する配布契約

- .NET 10環境でOS中立Core回帰テストとWindows `Meltia.exe --meltia-selftest` を実行する。
- 公式スクリプトに`-FrameworkDependent`を指定し、`-SkipSmokeTest`を使用しない。
- 展開したZIPに`Meltia.exe`、`LICENSE`、`THIRD-PARTY-NOTICES.md`、`MELTIA_LICENSE_POLICY.md`、`PREVIEW_RELEASE_NOTES.md`、`meltia-preview-selftest.json`が存在し、セルフテスト結果が`Passed: true`であることをチェックする。
- ZIPから`Meltype.exe`、本家TSF用バイナリ・インストールスクリプト、および画像素材を除外。本人以外の商標を公式版であるかのように宣伝しない。
- ZIPのSHA-256を生成し、配布する。署名済みインストーラー・実機入力の保証は宣言しない。
- データの収集、オンライン認証、保存先移行、アップデートはこのリリースでは提供しない。

## 未確定・残る権利上の確認

1. 継承リポジトリに含まれる**本家Meltypeロゴの利用許諾は明確に確認できていない**。この配布ZIPには含めないが、GitHubリポジトリ内の既存画像の権利関係は別途確認が必要。
2. `THIRD-PARTY-NOTICES.md`は継承元の製品構成を主として説明している。今回のZIPが実際に含む実行物について必要な表示を監査し、将来のMozc内蔵版やself-contained版では再監査する。
3. CC BY-SA辞書などの改変元データの著作者表示・ライセンス提供の法的十分性は、個々のデータ配布実態と照合が必要。文書確認のみで最終法的適合性を保証しない。
4. GitHub Actionsジョブがステップ0で失敗する事象があった。ステップ0は成功ではなく**未実行**として扱い、公開CIの緑と生成物の検証をRelease条件にする。

この監査は技術的な公開前チェックであり、法律専門家による正式な適法性意見ではありません。
