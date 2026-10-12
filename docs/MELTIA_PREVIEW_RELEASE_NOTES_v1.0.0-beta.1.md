# Meltia v1.0.0-beta.1 — Windows Keyboard Preview

> **Pre-release / 実験用** — 正式なMeltia IMEではありません。Windows向けのキーボードフック方式プレビューです。
>
> This is an experimental keyboard preview, **not** a registered Windows TSF IME. No production-quality or security guarantee is made.

## このリリースについて

MeltiaはMeltypeから派生した独立のオープンソースプロジェクトです。本家Meltypeによる公式リリースではありません。このPreviewはMeltypeのC# Windows入力・変換ボックス・辞書処理を再利用しつつ、Meltiaの独立した境界判定Coreを実験的に接続します。

- `Meltia.exe` として独立起動（Windows x64）。
- 日本語／英語の混在判定、かな漢字変換、候補選択、キーボード操作を試験できます。
- 新境界判定はトレイメニューから **任意に有効化**できます（初期OFF、環境変数 `MELTIA_BOUNDARY_PREVIEW=1` で起動時有効化可能）。
- `commit`・`meltiawotamesu` の入力不具合に対するPreview専用の語彙保護処理を含みます。ただし、一般的な日英境界の正確性は保証しません。
- 新方式の不確実な候補は原則として既存処理に戻します。ユーザー操作による確定結果まで常に正しいという意味ではありません。
- プレビューのユーザーデータは原則 `%LOCALAPPDATA%\Meltia\Preview` に保存されます。本家Meltypeのデータ・TSF登録・自動更新は変更しません。

## 利用方法

1. Assets内の `meltia-windows-preview-win-x64-framework-dependent.zip` を取得し、任意のフォルダーにすべて展開する。
2. **Windows 10/11（x64）と .NET 10 Windows Desktop Runtime** をインストールする。フレームワーク依存構成なので、.NETランタイムはZIPに同梱されません。
3. **Meltypeが起動している場合は終了する。** 両製品のWindowsキーボードフックの多重起動を回避するため、同時使用できません。
4. `Meltia.exe` を起動し、Windowsトレイから操作する。正式IMEとしての登録作業は不要・非対応です。
5. 試験は保存不要なテキストで行い、問題が出た場合は実験判定をOFFにして終了する。

ZIPと同時にSHA-256検証用 `.sha256` ファイルを提供します。署名されたインストーラーはありません。

## 既知の制約

- この版はWindowsキーボードフック方式のみ。**Meltia専用TSF IMEのGUID・登録・インストーラーは未実装**であり、旧Meltypeの登録スクリプトをMeltiaとして実行してはいけません。
- キー入力の外部アプリへの影響、入力欄ごとの相性、キーボード配列、アクセシビリティ、権限の異なるウィンドウは実機による広範な評価が未了です。
- 同じキー列が日本語／英語双方に成立する場合、利用者の意図を自動的に特定できません。例：`tokyo` / `ime`。
- `commit`、`japaneseno`、`meltiawotamesu` などの混在入力は重点検証対象です。個別の成功を一般的な正答率と解釈しないでください。
- 公開CIでWindowsビルド・セルフテスト・回帰テストが成功したコミットだけをAssets付きのPreview Releaseとして公開します。自動試験では実際のキーボードフック入力E2Eを完全にはカバーしません。
- `Meltia.Core` の研究用バージョン番号と、この配布用Windowsアプリの `v1.0.0-beta.1` は対象が異なります。開発中の研究成果すべてが実際の入力に採用されているわけではありません。

## ソースとライセンス

- ソースコード：このReleaseが指すコミットのGitHub自動生成Source codeアーカイブ、または `https://github.com/Fugu0141/Meltia`
- Meltia/継承Meltypeプログラム本体：**GPL-3.0-or-later**
- 変更・原作者情報：Git履歴、ソース各ファイルの著作権表示
- 辞書・その他の再配布物：`THIRD-PARTY-NOTICES.md`、`MELTIA_LICENSE_POLICY.md`
- Meltypeの画像・ロゴには独立した権利関係があるため、**このPreviewバイナリZIPには継承した画像素材を同梱しません**。

詳細は [Meltiaのライセンス方針](https://github.com/Fugu0141/Meltia/blob/release/v1.0.0-beta.1-preview/docs/MELTIA_LICENSE_POLICY.md) と [Previewの操作上の注意](https://github.com/Fugu0141/Meltia/blob/release/v1.0.0-beta.1-preview/docs/MELTIA_WINDOWS_PREVIEW.md) を参照してください。

## Feedback

不具合報告にはWindowsのバージョン、入力キー列、期待する日本語/英語表示、実際の結果、新境界判定ON/OFFを添えて、[Meltia Issues](https://github.com/Fugu0141/Meltia/issues) へお願いします。パスワードや個人情報を含む文字列は公開しないでください。
