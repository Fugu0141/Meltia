# R2 / T3c-A — 無登録のNative↔C# Named Pipe実通信テスト（2026-10-20）

> **Windows実IME / TSF登録の証明ではない。** PR #81 (T3b) の次段階として、既存の隔離Native `PipeClient` と `.NET NamedPipeServerStream` の相互通信を **実プロセスと実パイプ** で検証する。登録、IME選択、キーフック、Meltia.exe通常起動・アップデート・配布の変更は一切含めない。

関連: [正式IME化ゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md) / [T3b無登録IPC](MELTIA_R2_T3B_ISOLATED_IPC_20261018.md) / [Issue #16](https://github.com/Fugu0141/Meltia/issues/16)。

## 目的と実装範囲

- `tools/prepare_meltia_tip_stage.py` を使用し、上流の `native/tip/Pipe.cpp` を**変更せず**別の一時ディレクトリに隔離生成する（T2 manifest由来のパイプ名、接続世代管理を保持）。
- `tools/test_meltia_t3c_native_pipe.cpp` を、隔離生成された実 `Pipe.cpp` とリンクする**テスト専用コンソールEXE**としてx64/x86双方でコンパイル。Native側は継承 `PipeClient::Transact` / `Close` をそのまま呼び、全応答をバイト列として検査する。
- `src/Meltia.TipIpcFixture` は**テスト専用**サーバープロセス。C#側で `MeltiaTipIdentity.PipePrefix + Windows SID` をバインドし、1要求1返信のWindows Message-mode Named Pipeを提供する。1接続に1つの既存 `MeltiaTipWire` を使う。
- ディスパッチは固定の合成応答であり、実 `TipServer.Handle`、`MeltypeSession`、辞書、Composition、`TextService` のTSFイベントを実行しない。**製品用の専用サーバー起動配線も未実装**。
- 検証プロセスと隔離生成物は試験後に終了・消去。レジストリ、COM、IME登録先、既存Keyboard Preview、本家Meltypeデータには触れない。

## 受け入れマトリクス

| 試験 | 期待結果 |
| --- | --- |
| Aの1番目（hello v1） | `active:true`、応答に数値`version:1` |
| Aの2番目（日本語UTF-8を含むkey） | SIDと文字列をC#で検証し、active応答 |
| Aの3番目（version 2のkey） | ディスパッチせずinactive、認証状態破棄 |
| Aの4番目（helloなしのversion 1 key） | 以前の認証は復元せずinactive |
| Aの5〜6番目（再hello、key） | 再認証後のkeyのみactive |
| Bの1番目（新しい接続のhello前key） | 接続Aの認証を継承せずinactive |
| Bの2〜3番目（hello、key） | 新しい接続で再認証後のみactive |
| x64 / x86 | 同じC#サーバーfixtureと、各アーキテクチャの実Win32クライアントを実行 |
| 異常終了・タイムアウト | nonzero終了、CI失敗。serverを終了し一時生成物削除 |

返信フィールドは `version/active/consumed/commits/view` のJSON形状を固定して照合。**接続数2・往復9件×2アーキテクチャ**を確認する。結果の「実通信」はこのfixtureの範囲のみを指す。

## 実行手順

Windows + Python + .NET SDK 10 + Visual Studio C++ x64/x86 Build Tools:

```powershell
python tools/generate_meltia_tip_identity.py --check
python tools/test_meltia_t3b_protocol.py
dotnet run --project src/Meltia.TipProtocol.Tests/Meltia.TipProtocol.Tests.csproj -c Release
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3c-pipe.ps1 -Architecture Both
```

`Meltia Windows Preview CI` のWindowsジョブに追加。**実装提出時点では手元にWindows/.NET SDKがないため、テスト成功を未確認として扱い、PRのWindows Actions実行結果で検証する。** これまでのT3b静的/C#試験やT3a無登録DLLビルドも引き続き必須。

## このPRで保証しないことと次工程

1. `TipServer` の本物の`Serve` / `Handle`、プロセスの本番ライフサイクルへの起動経路、UIスレッドInvoke、セッション破棄と確定副作用は未検証。**T3c-B**でMeltia専用ホストとして配線し、無登録のエンドツーエンド・セッションをテストする。
2. `TextService::Request` の接続世代 hello自動再送、文字喪失/二重入力、実Windows TSF、候補確定/日本語変換、昇格/低整合性や他アプリとの接続は未検証。T4/T5で隔離Windows VMの実IME登録・実打鍵E2Eをレビュー承認後に実施。
3. パイプ通信fixtureにはWin32 API実通信が必要で、Linux上で再現できる契約テストではない。CI失敗または未実行時はT3c-A完了扱いにしない。
4. Nativeの元コードに存在する追加の信頼・アクセス制御条件は本PRで変更せず、この接続テストのみをもって認可設計全体が安全と判定しない。

## 初回GitHub Actions実測（2026-10-20）

検証対象HEAD: `5d09d5230eb8ff9d60622f3cc2f30f7847f4837a`（下記記録を追記する直前のコード変更HEAD）。

- [Meltia Windows Preview CI #198](https://github.com/Fugu0141/Meltia/actions/runs/39397680505): **success**。WindowsジョブのT3c-A実Native↔C# Named Pipeテスト（x64・x86）、T3b、T3a、継承IPC契約、Previewビルド/無フック検証/監査の全工程が成功。
- 同CIのLinux研究品質ジョブ: **success**（1,067合成ケースと回帰品質ゲート）。
- [meltia-strict-quality #181](https://github.com/Fugu0141/Meltia/actions/runs/39397680540): **success**（Windows/Linux双方）。
- C++↔C#実通信がCIで確認できたのは、**テスト専用ホストと既存PipeClient間**であり、実 `TipServer` / `TextService` / TSF上での成功を意味しない。

## レビュー上の停止条件

- 自動登録、`regsvr32`、レジストリ変更、既存 `src/Meltia.Windows` の既定モード変更が混入したら差し戻す。
- x64/x86のいずれかが未実施/失敗、またはT1/T3b/Windows Preview CIで既存回帰が出たらMergeしない。
- ネイティブexeおよびC# fixtureを通常インストーラー・ZIP・リリースへ含めない。
