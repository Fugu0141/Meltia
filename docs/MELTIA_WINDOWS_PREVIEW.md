# Meltia Windows Keyboard Preview — 最初の実行可能アプリ構成

> 状態：**Windows向け試験版ソースを実装し、GitHub ActionsのWindowsランナーでビルド・無フックのセルフテスト・ZIP監査に成功**。ただし実キー入力・編集・確定のWindows E2Eは未検証。
> 正式なWindows TSF IME、独立インストーラー、署名済みリリースではない。
> ソース版 `1.0.0-beta.1`（Windows Keyboard Preview）。公開済みbeta.1アセットは当時の候補コミットからビルドされており、その後の修正を自動で含まない。

## 何を再利用したか

Meltype本家の既存Windows C#ソースを新しい
`src/Meltia.Windows/Meltia.Windows.csproj`から**ソースリンクで再利用**する。
再実装せず活用するものは次のとおり：

- Windowsキー入力の受付と再入力、フォーカス追跡
- 変換ボックス、入力中の表示・候補、編集・取り消し・確定処理
- 日本語ローマ字、辞書・学習、Microsoft IME/Mozcを利用する漢字変換
- トレイ、入力のON/OFF、設定画面、ユーザー辞書・バックアップ
- Meltype.Coreの既存ユニットテスト群（本家の動作確認は別途）

**原著作権表示とGPL-3.0-or-laterを保持**し、MeltypeをMeltiaが作ったと
誤表示しない。新しい実行ファイル名は `Meltia.exe`。
開発中の内側の名前空間・ダイアログ等にMeltypeの名前は残っている。

## どこがMeltia独自か

`Meltia.Core`の研究用境界判定器を
`Meltype.Core/Composition/CompositionDetector.BoundaryRefinement`を通じて
**実際のWindows変換ボックスの区間判定に接続**する。

`MeltiaPreviewBoundary.Refine`には、入力中のMeltype区間を
`MeltypeBoundaryShadowBridge`でMeltiaの証拠に変換する処理がある。

- トレイメニュー「**Meltia 新境界判定を試す (実験)**」からON/OFFする。
  **初期値はOFF**。プロセスを再起動するとOFFに戻る。
  起動時に `MELTIA_BOUNDARY_PREVIEW=1` を設定した場合のみ初期ON。
- 検討対象は英字のみ・5～128 UTF-16単位・ローマ字の未完部分がない入力。
  手動判定モード、保護されたURL/メール、記号、長文、仮名入力は
  元のMeltypeの処理を優先。
- Meltia Coreが候補数上限に達した、判断に確認が必要、Top2の得点差が
  1.5未満、既存Meltypeと同じ結論、漢字の1音分の単位をまたぐ等の場合は
  **元のMeltypeの区間判定に戻る**。
- 新方式が採用できる場合も**表示用の英語／日本語区間だけ**を変更し、
  原文・日本語の読み・UTF-16の単位・従来の候補変換と編集UIを維持。
- 新方式は日本語の確定・削除・システムIMEの状態を書き換えない。
  ただし、最終的に既存Meltypeのユーザー操作で確定した場合には、
  変更された表示内容が入力される可能性がある。
  **実使用前に退行・安全性を検証すること**。

## Meltypeからの独立・注意事項

- データ保存先は既定で `%LOCALAPPDATA%\Meltia\Preview`。
  `MELTIA_DATA_DIR`で任意の実験用フォルダーに変えられる。
  既存Meltypeの `%LOCALAPPDATA%\Meltype` は意図的に拒否する。
  `AutoIME`→Meltypeの旧データ移行も**行わない**。
- Windowsのスタートアップ登録の名前は
  `MeltiaPreview` とし、Meltypeの登録・ショートカットを変更しない。
- 終了のウィンドウメッセージ名は `Meltia.Preview.Exit`。
  他方を終了させる合図にならない。
- **Windowsフックの競合を防ぐため、MeltypeとMeltiaを同時には起動できない**。
  既存Meltypeと同じ単一起動Mutexをあえて共有する。
- Meltiaプレビューは**Keyboardモードに固定**し、
  本家の登録済みTSF TIPを使ったり、自動切替したりしない。
  MeltypeのNamedPipe/TSFサーバーは起動しない。
- **本家の自動更新は無効**。リポジトリへの報告リンクもMeltiaへ変更。
  本家のアンインストールスクリプトは使用しない。
- ネイティブTSF DLL `native/tip`はMeltype固有のGUIDを持つため
  Meltiaとして**登録・配布しない**。
  Meltypeインストーラーの名称・レジストリ・GUIDも転用しない。

## Windowsでのビルド・確認手順

前提：Windows 10/11、.NET **SDK 10**、PowerShell。
GitHub Actionsの利用枠は必要ない。

```powershell
git switch release/v1.0.0-beta.1-preview

# 自分のPCでリリース構成の実行ファイルをビルドし、
# フック未起動の統合スモークテストを実行してからZIPを作る
powershell -ExecutionPolicy Bypass -File .\tools\build-meltia-windows-preview.ps1
```

### NU1101: Windows Runtime Packが取得できない場合

リポジトリ直下の `nuget.config` はオフライン向けで
`<packageSources><clear /></packageSources>` を使うため、
自己完結型の`dotnet publish -r win-x64 --self-contained true`で必要となる
Windows用ランタイムパッケージを取得できない場合がある。

Meltia Previewでは本体設定を変えず、ビルドスクリプトの先頭で
`tools/meltia-publish.nuget.config` を使った**明示的なrestore**を行い、
その後 `--no-restore` でpublishする。
初回は `https://api.nuget.org/v3/index.json` へのネット接続が必要。
NuGetのユーザー全体の設定を変更する必要はない。

ネットワークの制限などで自己完結型パッケージを復元できない場合は、
開発PCに**.NET 10 Windows Desktop Runtime**がインストールされていれば
ランタイムを同梱しないテスト用構成を試せる：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-meltia-windows-preview.ps1 -FrameworkDependent
```

こちらは `artifacts/meltia-windows-preview-win-x64-framework-dependent.zip`
に保存され、実行先PCにランタイムが必要。
`-FrameworkDependent`は精度向上や正式IME登録とは無関係であり、
通信エラーや他の依存関係の問題を必ず解決できるとは限らない。

**以前の12件のNU1101はNuGet復元フェーズの失敗であり、C#コードのコンパイル結果ではない。**
GitHub Actions Windowsランナーで、フレームワーク依存のビルド・無フックセルフテスト・配布ZIP監査に成功した。開発者PCでの手動入力・フック動作の全項目は引き続き未検証。

フレームワーク依存構成（公開プレビューの対象）で生成・監査したファイル：

- `artifacts/meltia-windows-preview-win-x64-framework-dependent/Meltia.exe`
- `artifacts/meltia-windows-preview-win-x64-framework-dependent/meltia-preview-selftest.json`
- `artifacts/meltia-windows-preview-win-x64-framework-dependent.zip`

このスクリプトはビルドかテストに失敗した場合は
**ZIPを作成しない**。手動でスモークテストだけ行う場合：

```powershell
.\artifacts\meltia-windows-preview-win-x64-framework-dependent\Meltia.exe --meltia-selftest .\artifacts\meltia-smoke.json
```

正常に生成された実行ファイルを起動するとWindowsトレイで動作する。
**フックを持つMeltypeは先に終了すること**。
変換ボックスの入力・変換・Enter確定・Backspace・フォーカス移動の実機確認は
自動スモークとは**別に必要**。

実験モードを使う場合はトレイからONにする。
不審な動作があればすぐにOFFへ戻し、実際に打ち込む長文やパスワードは
テストしない。パスワード入力欄、遠隔操作、権限の異なるアプリ、
ゲーム、複数キーボード、Windowsタッチキーボードでも別途安全評価が必要。

## 製品として完成させる残作業

- [x] Meltypeの既存Windows入力・変換実装を別アプリのビルド構成に再利用
- [x] Meltia Coreの任意有効の区間結果を実際の表示へ橋渡し
- [x] 設定・終了制御・起動時登録・更新・TSFへの衝突を抑制
- [x] セルフホストビルド/試験付きZIP作成スクリプトを用意
- [x] **GitHub Actions Windowsランナーでの.NET SDK 10ビルド成功・無フックのスモーク結果**
- [ ] **Windows実入力・確定・変換・Backspace・操作停止のE2E試験**
- [x] 研究Coreの既存1,067合成ケース・ベンチマークとMeltype Core回帰をCIで検証（自然文holdoutとE2Eは別途必要）
- [ ] Windows TSFネイティブ側の独立GUID、パイプ名、レジストリ、
      インストーラーおよび確実なアンインストール
- [ ] 設定画面・ヘルプ・通知の本格的なMeltia表記への統一、
      学習・辞書・ログの分離とデータ移行ツール
- [ ] セキュリティ・配布する辞書/Mozc・ライセンス再確認、
      Crash recoveryと署名・更新導線
- [ ] 独立したユーザーの実入力を使った品質試験、正式リリース判定

**この段階は「実行可能なアプリ構成への移行」であり、製品完成の宣言ではない。**
Windows Keyboard Preview `1.0.0-beta.1` の研究開発段階。mainへのソース統合は製品版完成・Windows実機E2E合格・既存公開beta.1アセットの更新を意味しない。

## 実機入力で発見した英語の読み込み問題（2026-10-12）

Windows Previewの初回起動に成功した後、ユーザーから次の入力例の誤変換が報告された。

| 打鍵（スペースなし） | 報告された誤変換 | 確認する解釈 |
|---|---|---|
| `commit` | 「こっみてゃ」 | `commit`（英字のまま） |
| `meltiawotamesu` | 「目lチアを試す」 | `meltia`＋「をためす」（漢字変換後「を試す」） |

`commit` は継承した英語辞書に掲載されているにもかかわらず、
未確定ローマ字が正しい英語区間として保持されないことがある。
`meltia` は継承辞書にない製品名。これらは
単純にCoreの未知英語候補生成を増やすだけでは修正できないため、
`Meltia.Windows/MeltiaLexicalRecovery.cs` にPreview専用の保護処理を追加した。

- 既存辞書に掲載され、`c/l/q/v/x`など英語綴りの強い手掛かりを持つ
  5文字以上の語、またはMeltia自身の製品名だけを候補とする。
- 入力全体が強い英語候補であれば、`pending`の最後の子音も含めて
  **入力どおりのRawを英字で保持**する。
- 英語候補の直後に`wo/ni/de/ga/ha/to/no`の助詞から始まる
  日本語ローマ字が続き、その全体が読めるなら、
  英語と日本語に分離する。**元の入力単位境界をまたいで切らない**。
- ユーザーが明示的に日本語へ修正した記憶、保護区間、URL、
  日本語として普通に読める曖昧語`tokyo`/`ime`/`sake`を上書きしない。
- この安全側の辞書保護は**実験境界判定スイッチOFFでも有効**。
  一方、辞書から確定できない文の自由な日英境界推定は引き続き任意ON。
- `meltia`を小文字で入力した場合は`meltia`のまま保持する。
  `Meltia`と打った場合は大文字を保持。入力原文を勝手に書き換えない。

`--meltia-selftest`では従来のRaw一致に加え、
`CompositionText.Append`を1文字ずつ実行して英語`commit`、
`meltia`+日本語の`wotamesu`、先頭大文字版、
日本語`nihongowotamesu`、曖昧語の不変更をチェックする。

**状態：修正ソースと自動回帰テストは実装済み。**
GitHub Actions上のビルド・無フックセルフテストは成功済み。報告された誤変換に対する修正後のWindows実キー入力・確定の再評価は未完了。
次のローカルビルドが失敗した場合は作成中の
`meltia-preview-selftest.json`やコンソールのエラーを確認する。
