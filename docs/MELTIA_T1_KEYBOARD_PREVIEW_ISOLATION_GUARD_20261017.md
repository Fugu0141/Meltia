# T1-A — Meltia Keyboard Previewの隔離契約・静的回帰ガード（2026-10-17）

> 状態：実装・CIレビュー対象。上流コード受入れとMeltia TSF化に先立つ**Keyboard Previewの既定安全契約**を固定するための、ソース読み取り専用テスト。`MELTIA_PREVIEW`を外す・旧Meltype TIPを登録する・起動中の入力フックを有効化する目的ではない。

## 目的・背景

[上流変更の受入れ方針](MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md)は、本家のComposition／Host変更によってMeltia独立Previewが本家のTIP登録・更新・データ保存へ誤って接続されることを禁止する。既存の [Meltia Windows Previewセルフテスト](MELTIA_WINDOWS_PREVIEW.md) は実行可能な`Meltia.exe`を用いて無フック入力分割を検証しているが、上流コードのリファクタリングでビルド条件や起動コードを変更した場合の**構造契約**は別に維持する価値がある。

## 実装

- `tools/test_meltia_keyboard_preview_isolation.py`（Python標準ライブラリだけ、レジストリ・ネットワーク・ユーザーデータ・Windows APIを使用しない）：
  1. `Meltia.Windows.csproj` が独立した`Meltia`実行ファイルとして`MELTIA_PREVIEW`を定義し、継承したCompositionとMeltia Bridgeを使うが、`native/tip`や登録スクリプトをコンパイル対象に含めないこと。
  2. `Program.cs`の`#if MELTIA_PREVIEW`条件をテスト用に解析し、Previewビルドで`settings.Mode=Keyboard`、`AutoUpdate=false`、`Updater.ApplyStagedAtStartup`非実行、`AppPaths.MigrateFromOldName`非実行、隔離された`MeltiaPreviewSafety.Initialize()`が先行すること。
  3. `Startup.cs`のPreview側起動名が`MeltiaPreview`であり、Meltype本家を上書きしないこと。
  4. `MeltiaPreviewSafety`に本家データディレクトリ拒否・Preview専用ディレクトリ初期化の構造が保たれていること。
  5. Previewビルドスクリプトで`regsvr32`、`Register-Tip.ps1`、`MeltypeTip.dll`、`setup-ime.ps1`を実行／同梱するための明示文字列が現れないこと。
- `.github/workflows/meltia-windows-preview-ci.yml`でWindows Publish前にこのガードを実行。script自身の変更でCIが起動するようpaths-filterにも登録。

## 検証と制限

- **このテストは静的な意図・構成上の退行ガードであって、実際のWindowsキー処理やコンパイラによるすべての副作用の存在を否定する安全証明ではない。** シンボル条件や実行構造の変更に気づけるよう、解釈不能な`#if`は黙って通さず失敗させる。
- 上流の新しいコールサイト、間接実行、COM操作やパッケージ経路の全網羅は保証しない。実行時の入力損失、GUI、文字列の欠落/重複、低整合性やx86/x64のTIPは別のWindows実機E2Eで確認する。
- 本番IMEの実装時は`MELTIA_PREVIEW`既定動作を変更せず、別の明示的ビルド／設定フラグでTSFを用意する。ガードを消してテストを通過させるのではなく、**Keyboard Previewが今後も安全に動作すること**を確認する。

## 再実行

```powershell
py -3 tools/test_meltia_keyboard_preview_isolation.py
powershell -ExecutionPolicy Bypass -File .\tools\build-meltia-windows-preview.ps1 -FrameworkDependent
```

上段はOSを問わない**静的**テスト。下段はWindows向けビルドと既存の無フックsmoke。フックを有効にする実キーE2Eとは異なる。

## CI結果

- PRの最新HEADでGitHub Actionsが完了した後に結果を追記する。Windows実機手動試験は未実施である。
