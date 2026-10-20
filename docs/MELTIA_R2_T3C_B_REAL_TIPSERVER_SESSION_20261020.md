# R2 / T3c-B — 実TipServer・MeltypeSessionの無登録IPC接続検証（2026-10-20）

> 本成果は**テスト専用プロセス内で既存 `TipServer.Serve/Handle` と `MeltypeSession` を実行するもの**であり、製品のIMEホスト起動機能、TSF登録、実キー入力の完成ではない。テスト実行以外の用途で用いない。

## 目的・実装契約

前工程: [T3c-A](MELTIA_R2_T3C_A_REAL_PIPE_FIXTURE_20261020.md)（固定ディスパッチへの9往復・2接続検証）。本工程では固定応答に代えて、**継承した実際の `TipServer` がセッションを生成して処理した結果**を、隔離済みのネイティブ `PipeClient` で送受信する。正式IME化の親Issueは [#16](https://github.com/Fugu0141/Meltia/issues/16)。

- `TipServer` の依存は従来 `CompositionService` 固定であったため、内部のセッション生成を `Func<MeltypeSession>` に抽出する。既存の**公開3引数構築・実験4引数構築は変更せず**、`CompositionService.CreateSession` が呼ばれる経路を維持する。
- `src/Meltia.TipHost.Tests` はテスト専用Windowsプロセス。既存 `Meltype` アセンブリの `TipServer` を引用し、WinForms STAメッセージループで `Control.Invoke` の実行を受ける。入力フック、IME選択、COM登録や`Meltia.exe`起動はしない。
- テスト中に限り**メモリ内の** `Settings.Mode = Tsf` とし、`Active` を検証する。ファイルに設定を書き出さず、`MELTYPE_DATA_DIR` は一時ディレクトリに限定する。検出器と`MeltypeSession`は既存Coreを使用。漢字変換器のみ外部OS/サービスアクセスを避ける固定null実装とする。
- `tools/test_meltia_t3c_b_tipserver.cpp` はT3aの隔離ステージで生成される実 `Pipe.cpp` を利用する。**同一 `PipeClient`** で`Close()`→再接続し、`ConnectionGeneration()`の増加まで検証する。
- テスト用プロセス/NativeコンソールEXEは配布対象外。PowerShellは生成物を一時パスから削除し、環境変数を元に戻す。

## 受け入れ条件

| ケース | 期待 |
| --- | --- |
| 接続A・hello前key | fail closed（inactive） |
| hello v1 | active |
| `k`→`a` | 実 `MeltypeSession.HandleKey` の応答に表示中 `view` がある |
| commit | 実セッションで変換途中を確定する応答 |
| close | inactive、セッション辞書から破棄 |
| close後の`composing:true` | 旧セッションを復活させずinactive |
| close後の通常key | 別セッションが作られる |
| 接続B・同じクライアントの再接続 | 接続世代が増加し、hello前keyはinactive |
| 接続B・`composing:true` | 古い入力続きを採用しない |
| 接続B・hello後通常key→close | 新セッションを開始・破棄 |
| 実セッション生成数 | 全14往復/2接続で**3件**、未認証や保護要求では増えない |
| x64/x86 | 双方で同じシナリオを実Windows Named Pipe経由で合格 |
| テストホスト | STA UI Invoke・終了通知・45秒のタイムアウト・確実な子プロセス終了 |
| 本家共存 | 本家GUID/レジストリ・保存データ・Keyboard Previewの通常起動に変更を加えない |

C# WinForms制御用 `InternalsVisibleTo` は`Meltia.TipHost.Tests`に限定し、public製品APIを増やさない。失敗・異常終了時はnonzero exitとしてCI失敗にする。

## 実行方法

Windows / .NET SDK 10 / Visual Studio C++ x86/x64 / Python:

```powershell
python tools/generate_meltia_tip_identity.py --check
python tools/test_meltia_t3b_protocol.py
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3c-pipe.ps1 -Architecture Both
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3c-b-host.ps1 -Architecture Both
```

`.github/workflows/meltia-windows-preview-ci.yml` のWindowsジョブに本実通信ゲートを追加した。**PR作成時点ではコードの動作実測は未確定。CI実行結果を確認したうえでレビュー・Merge判断すること。**

## 初回CI実測（2026-10-20）

ソース実装HEAD: `a55fc24c6add53f68dc052e2ccc26c6c71ee2ee5`（この追記より前のコードHEAD）。

- [Meltia Windows Preview CI](https://github.com/Fugu0141/Meltia/actions/runs/39400294710): **Windows・研究品質ともsuccess**。T3a/T3b/T3c-A・T3c-B・Preview・継承Coreを含めたWindowsジョブが完了。
- WindowsログでT3c-Bは **x64/x86の両方で14往復・同一Nativeインスタンスの接続世代`1 → 2`・3件の実`MeltypeSession`** を確認（プロセス間Named Pipe）。
- [meltia-strict-quality](https://github.com/Fugu0141/Meltia/actions/runs/39400294689): **Windows/Linux双方success**。
- 試験は実際の`TipServer.Serve/Handle`を対象とするが、Windowsの登録・実打鍵・IME有効化には進んでいない。研究品質の成功とIME実機品質の合格を混同しない。

## PR #83 レビュー指摘に対する検証強化（2026-10-20）

レビュー指摘: [実セッションE2Eの成功条件がJSONの断片一致のみ](https://github.com/Fugu0141/Meltia/pull/83#discussion_r4323563449)。従来は `consumed:false`、空の`commits[0].text`、誤った`deleteBefore`、または`ka`が変換されていない状態でも成功してしまう可能性があった。

**解決策:** 新規パーサーを独自実装せず、隔離ステージの既存Native `Json.cpp/Json.h` をNativeテスト実行ファイルにリンク。v1返信JSONを構造でパースし、次の契約を型付きで照合する。

- 全返信: `version` は数値1、`active/consumed` は真偽値、`commits` は配列、`view` はobject/null。検証対象外の偽装フィールドは受け付けない。
- handshake前、`close`、失効セッション: `active:false`、`consumed:false`、空`commits`、`view:null`。
- hello: `active:true`、`consumed:false`、空`commits`、`view:null`。
- 最初の `k`: `active:true`、`consumed:true`、`view.text` が非空、確定編集なし。
- 次の `a`: `active:true`、`consumed:true`、`view.text == "か"`、確定編集なし。
- commit: `active:true`、`consumed:true`、`commits` 1件の`text == "か"`、`deleteBefore == 0`、`view:null`。
- 偽の返信を使う**失敗確認テスト**を追加: `consumed:false`、空確定、`deleteBefore:2`、`view.text:"ka"`、文字列型の`version` はいずれも成功条件を満たさない。

`Json.cpp`の使用に必要なUTF-8変換はコンソールテスト側のみで提供する。新しい本番依存・配布ファイルは増やさない。**修正後の実測:** [Windows Preview CI #](https://github.com/Fugu0141/Meltia/actions/runs/39402952379) は `0f32308ebfe735a2630929612e71b40b22c02ed3` に対し **Windowsジョブ/研究品質ともsuccess**。T3c-B x64/x86の型付きJSON確認・負のテスト・継承Preview監査を通過。[Strict Quality](https://github.com/Fugu0141/Meltia/actions/runs/39402952353) もWindows/Linuxとも **success**。この追記は結果記録のみで、本番IME実機E2Eの完了を意味しない。

## 検証できないこと・続く工程

- 新規ホストはあくまで**試験実行のためのホスト**である。通常起動時の製品ホスト独立、フォーカス同期、起動競合、常駐ライフサイクル、正規の登録先などは実装・証明しない。
- `TextService::Request` 自身のhello自動再送・編集セッション更新や本物の入力アプリ（メモ帳/ブラウザー）との実E2Eではない。Windows TSFは未登録。
- `TipServer.Handle` が返すJSONフレームとセッション境界を検証するが、かな漢字辞書/候補品質、キーボード配列、管理者・AppContainer権限差の全条件を網羅するものではない。
- 次の候補は専用製品ホストの隔離ライフサイクル設計、および`TextService::Request`の実経路の無登録検証。その後レビューを通してT4/T5の隔離VM登録実験を行う。
- [PR #82レビューの非blocking指摘](https://github.com/Fugu0141/Meltia/pull/82)（同一PipeClientインスタンスの再接続）は本工程で実パイプ検証に追加した。
