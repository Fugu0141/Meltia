# R2 / T3d — Native `TextService::Request` の無登録・実 IPC 結合ゲート（2026-10-20）

> **Meltia の製品 IME はまだ完成していない。** この試験は Windows の OS/TSF に登録せず、隔離生成した Native `TextService.cpp` の **実際の `Request` メソッド**から、実 `PipeClient` → Windows Named Pipe → 実 `TipServer.Serve/Handle` → `MeltypeSession` へアクセスする。キーボードの OS イベントや TSF `ActivateEx/OnKeyDown/Apply` の E2E は行わない。

関連: [Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [T3c-B](MELTIA_R2_T3C_B_REAL_TIPSERVER_SESSION_20261020.md) / [T3b](MELTIA_R2_T3B_ISOLATED_IPC_20261018.md) / [T4/T5ゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 要件と設計

- T3c-B の固定 JSON 送信クライアントでは試せなかった、`TextService::Request` が新規接続で `hello` を自動実行する処理を**実 C++ メソッド**で検証する。
- `native/tip/*` は変更しない。隔離生成した `TextService.h` にのみ `MELTIA_T3D_REQUEST_TEST` マクロ付き friend プローブを加える。通常の隔離 DLL ビルドにはテスト経路が入らず、旧 Meltype に変更なし。
- テスト用コンソールは実 `TextService.cpp` と関連 `CandidateWindow/DisplayAttributes/LangBar/Pipe/Json` をリンクし、`TextService` を生成するが、COM 登録・`ActivateEx`・フォーカス操作・`OnKeyDown`・キーフック・TSF 編集は呼ばない。
- C# 側は T3c-B と同じ実 `TipServer` のテストホスト。セッション数のみ既存値3（T3c-B）と追加値2（T3d）を選べるようにする。ホスト起動時の `Settings.Mode = Tsf` はメモリ内だけで、`MELTYPE_DATA_DIR` は毎回一時ディレクトリへ隔離する。
- 同じ `TextService` と内包 `PipeClient` で切断・再接続し、接続世代の増加を確認する。サーバーで失効した `composing:true` は拒否し、新規 `composing:false` の入力だけが新しいセッションを作る。
- Windows x64 と x86 の実 Win32 Named Pipe 通信を両方実行し、いずれかの失敗時に nonzero とする。

## 受け入れ条件

| 操作 | 応答・状態の期待 |
| --- | --- |
| A: hello を明示せず最初の `key k` | `Request` 内部で hello v1 → `active:true`, `consumed:true`, 非空 `view.text` |
| A: 続けて `key a` | `view.text == "か"`, 空 `commits` |
| A: `commit` | `commits[0].text == "か"`, `deleteBefore == 0`, `view:null` |
| A: `close` | inactive; C# サーバーのセッション破棄 |
| B: 同じ `TextService` の `PipeClient.Close` → `composing:true` | 接続世代増加・自動 hello・失効中の入力を拒否 |
| B: `composing:false` の `key k` → `close` | 新しい実セッションが1件作られ、closeで破棄される |
| C# のセッション生成数 | 2件 (AとB各1件) |
| 品質 | 各応答の JSON フィールド型・version 1・active/consumed・表示・確定を照合 |
| 隔離 | 本家 C#/Native 原本・登録識別子・パイプ名・ユーザー辞書・通常 Keyboard Preview を変更しない |

実行手順:

```powershell
python tools/test_meltia_t3b_protocol.py
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3d-request.ps1 -Architecture Both
```

CI: `.github/workflows/meltia-windows-preview-ci.yml` に T3d を追加。**実測は Actions の Windows x64/x86 実行完了後に記録すること。** Linux 環境のみでは Windows Named Pipe と TSF ネイティブ実装の合格判定はできない。

## 未完了と次工程

1. Native `Request` の実際の IPC ゲートであり、**TSF が OS 上でキーを受け取り、アプリに文字を反映できることは証明しない**。テストが通っても製品の登録/有効化/配布を解禁しない。
2. 製品 Meltia 専用の常駐ホスト、モード切替/Mutex/起動競合/終了復旧のライフサイクルは未実装。次に**専用ホストの設計と無登録起動ゲート**へ進む。
3. T4/T5 の隔離 Windows VM で行う本当の登録・実打鍵・アプリ横断・権限別の確認は、独立レビュー後に別の PR として実施する。失敗時の文字の欠落/重複の品質ゲートは残る。
