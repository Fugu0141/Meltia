# R2 / T3b — Meltia専用TIP IPC v1の隔離実装（2026-10-18）

> **登録・有効化・配布はしない。** T3aの隔離DLLにv1の送受信チェックを追加し、継承TipServerのトランスポートをMeltia専用名で明示的に再利用できるようにする。Windows Keyboard Previewの標準動作はKeyboardのまま。Windows TSF実機E2Eは未検証。

## 要件と実装上の所有境界

- T2のconfig/tsf/meltia-t2-identity.jsonを唯一の識別子ソースとし、C#定数はtools/generate_meltia_tip_identity.pyで生成・CIの--checkで検証する。NativeのPipe文字列・バージョンは一時ステージ生成時に同じマニフェストから導出する。
- native/tip/には**一切変更を加えない**。prepare_meltia_tip_stage.pyは既知箇所を厳密照合し、隔離出力で要求の全JSONオブジェクトにversion:1を付け、応答にも数値のversion:1が存在する場合だけTSFへ処理結果を返す。不一致や不正応答はfalse。
- C#のTipServerに**明示的実験コンストラクター**を追加。既存3引数コンストラクターは本家Meltype専用Pipeと無バージョン契約を維持する。実験経路のPipeだけMeltia.Tip.v1.<SID>を使用。
- 1接続に1個のMeltiaTipWireを割り当て、数値version:1のhelloに対する既存ハンドラーのactive:trueが得られるまでkey/commit/select/closeは処理しない。未知version/operation、JSON異常、空SIDはactive:false, consumed:false, commits:[], view:nullへ拒否する。無効versionやJSONではその接続のhandshake状態を破棄する。
- セッションの実体は既存MeltypeSession、応答フィールドとhost副作用は既存TipServer.Handleを再利用。プロトコルゲート単体テストではFakeハンドラーを使うため、別のCompositionロジックは作らない。
- 本家資産のTSF GUID、TIP登録、Native GUID、登録/解除コマンドには変更なし。**Meltia専用サーバーを通常起動する配線は含めない**。PreviewはKeyboard固定。DllRegisterServerとDllUnregisterServerは引き続きSELFREG_E_CLASS。

## 実行方法と検証ゲート

Windows + .NET SDK 10、PythonとC++ Build Toolsで次を実行する。

    python tools/generate_meltia_tip_identity.py --check
    python tools/test_meltia_t3b_protocol.py
    dotnet run --project src/Meltia.TipProtocol.Tests -c Release
    python tools/test_meltia_t3a_stage.py
    powershell -ExecutionPolicy Bypass -File tools/build-meltia-tip-no-registration.ps1 -Architecture Both

C#単体テストでは、未知/欠損/非整数version、hello前key、inactive hello、4種の後続操作のレスポンス互換、未知operationと空SID、途中version変更、JSON破損、接続間の許可分離を確認する。Pythonテストは生成識別子、Native要求/応答、禁止登録、継承元非変更、変異した上流コードの停止を検査する。Windows CIでは上記と既存T1-B/R1/Preview/研究品質を同時実行する。

## 既知の未完了ゲート

1. **独立したMeltiaのホストライフサイクルと専用C#サーバーの起動**は未接続。既存Previewと同時にTSF/Keyboardフックを起動させないプロセス構成・モード分離が必要。Previewから実験コンストラクターを呼び出してはいけない。
2. 無登録DLLの本物のC++→Named Pipe→C#フレーム送受信をWindows上で検証する専用fixtureは今後必要。今回のC#テストは**オフラインのプロトコルテスト**であり、x86/x64 DLLとC# Pipeの実通信を証明しない。
3. Native側のパススルー・入力保持、応答順序、SID/Integrity権限差、ダウンタイム時の二重入力・文字消失、Windowsの実IME登録/解除、本家共存はT4〜T5の隔離Windows VMで別に検証する。
4. 本家コードを一時ステージ生成で機械書換する構成は暫定的。R2の責務分離後、Meltia固有のビルド可能ソースへ置き換える。

レビュー前にmainへマージしない。CIを確認してレビュー指摘を受けてから次段階へ進む。
