# Meltia Windows TSF/IME化：現行ソースからの移行準備と未実装ゲート（2026-10-17）

> **設計と調査の記録のみ。IME用レジストリ・COM・フック・DLLのビルド／登録／配布は本PRでは変更・実行しない。** 現行のWindows Keyboard Previewは`MELTIA_PREVIEW`で明示的にKeyboard固定・上流TIP無効・本家更新停止の設計。上流v1.2.1へのバージョン追従をIME開発の**前提条件とはしない**が、現行Native TIPをそのままMeltiaとして登録することは禁止する。

関連：[現行Windows Preview](MELTIA_WINDOWS_PREVIEW.md) / [Meltype受け入れ方針](MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md) / [R0 v1.2.1棚卸し](MELTIA_MELTYPE_V121_R0_FILE_INVENTORY_20261017.md) / [開発版の区別](MELTIA_VERSIONING.md)。

## 1. バージョン更新は必要か

**技術的にはMeltypeをv1.2.1やv1.2.2へ丸ごと更新せず、Meltiaの継承したC#/Native TIPソースを元に独立IMEを設計・実装できる可能性がある**。Windows TSFに登録可能かはGUID・COM DLL・ネイティブ/C#プロトコル・権限/安全性・ビルドと実機試験で決まるため、Meltype側の「特定のバージョン以上」という必須ゲートは現時点のコードから確認できない。

ただし、以下を分ける：
- **Meltypeの上流コードを新しいタグへ追従**：必須ではない。既知の重大バグやTSF回帰修正が必要なら、R0/R1を通して**変更単位**で移植する。
- **Meltiaアプリのバージョン番号を上げる**：開発中の設計・試験だけなら不要。**IMEを有効化して新たなテスト配布物を発行する際**は、既存公開Keyboard Preview `1.0.0-beta.1` と混同しない**新規プレリリース識別子**・リリースノート・互換性・ロールバック手順が必要。番号はリリース要件定義で決める。
- **Meltia.Coreの研究開発版** `v1.0-dev` と **Windows Preview** `1.0.0-beta.1` と **継承されたMeltype.Core** `1.1.1` は別。アップグレード番号の変更のみでIMEが有効になることはない。
- この判断は「現行IMEが動く」という意味ではない。正式TSF/IMEは未実装・未検証、Previewのhook-free CIは実入力E2Eの代わりにならない。

## 2. 具体的な現状の境界

| 対象 | 現時点の根拠 | 独立IME化のブロッカー |
| --- | --- | --- |
| `src/Meltia.Windows/Meltia.Windows.csproj` | `MELTIA_PREVIEW`定義、Meltype Windowsの`../Meltype/**/*.cs`をソース再コンパイル。`RootNamespace=Meltype`、`AssemblyName=Meltia` | 本家Winホストのコード更新が直接影響する。IME専用ビルドの分離が必要 |
| `src/Meltype/Program.cs` | Previewの`settings.Mode = InputMode.Keyboard`、上流TIP/Updaterを遮断、`Local\\Meltype.SingleInstance`mutex共有 | 単純にTsfへ変更すると本家TIPを参照する恐れ。IME用ビルドは**別の明示的実験ターゲット**として安全検証を組み込み、同居時のフック/Mutex動作を定義する |
| `src/Meltype.Core/Config/AppPaths.cs`／`src/Meltia.Windows/MeltiaPreviewSafety.cs` | `MELTIA_DATA_DIR`を`MELTYPE_DATA_DIR`へ渡すPreview用隔離。本家`%LOCALAPPDATA%\\Meltype`への書き込み拒否 | IMEモードでも設定/学習/辞書の独立、旧データ無断移行禁止を継続 |
| `native/tip/Globals.h` | 本家固定CLSID `{417D801B-A9BD-4C26-BD16-356A825A6998}`、Profile GUID `{21F643F4-72D5-4946-BF70-0A126AB52D05}`、その他3属性GUID、本家表示名 | **新たなMeltia専用GUID群**を発行。コード・C#側・登録/解除・アイコン/表示名で一致させる。既存本家GUIDを再利用しない |
| `native/tip/Pipe.cpp`／`src/Meltype/Tip/TipServer.cs` | `\\\\.\\pipe\\Meltype.Tip.<SID>` と `Meltype.Tip.<SID>` を使用。C#側で本家GUIDに基づく登録検出とアクティベート | Meltia専用のパイプ名前空間とプロトコル、送受信・サーバー認可・接続失敗時のfail-closed挙動を固定。名前の文字列置換だけでは不可 |
| `native/tip/TextService.cpp`／`native/tip/TextService.h` | ネイティブTIPのキー入力・編集・候補・コミット・再送を担当。`Meltype.exe`の自己プロセス検出も含む | `Meltia.exe`/自プロセスの扱い、候補Window、IME編集セッションの順序、失敗時の文字喪失・重複防止を実機で確認 |
| `native/tip/Dll.cpp` | CLSID COMのHKLM登録とTSF Profile/RegisterCategoriesを直接扱う | **Meltia固有の登録/解除対象だけを変更**することを証明。両者が同居した場合にMeltypeの登録・プロファイルに触れない |
| `native/tip/Register-Tip.ps1`／`native/tip/build.ps1` | `C:\\Program Files\\Meltype\\tip`へ x86/x64の`MeltypeTip.dll`を配置・`regsvr32`、本家CLSID削除処理 | 新規のMeltia専用インストール/削除・成果物・検証用dry-run。**このスクリプトをMeltia用途で実行しない** |
| `src/Meltype/MeltypeEngine.cs`／トレイ・設定 | `InputMode.Tsf` と `TipServer.IsRegistered` 等の分岐があり、本家モードの動作を前提に設計されている | Meltia独自TIPの登録判定、設定状態、起動/停止、モード切替、クラッシュ復旧を別テストで検証 |
| Windows Preview既存テスト | .NET 10 / Windows CIでビルド・hook-freeセルフテスト・1,067合成例が成功 | Windows TSF登録／実キー入力／MS Word、ブラウザー、管理者昇格/権限差、実Windows 10/11 E2Eはまだ必要 |

## 3. 段階と先行関係（番号は仮）

| 順序 | 作業・成果物 | この版のままで可能か | 許可される操作 |
| --- | --- | --- | --- |
| T0 | **本家・Meltiaの識別子／IPC／データ保存先／配布導線を棚卸しして凍結**。本書・契約図・変更マトリクス | **可能（本PRの文書）** | 静的調査のみ |
| T1 | 純粋ロジック・TSF要求/応答の契約、通常Keyboard起動の回帰、設定・IME登録の**未実行**ガード試験 | **可能** | Core/契約試験・静的検査のみ |
| T2 | Meltia専用GUID/表示名/パイプ/インストールパスの**設計**、C#⇔native接続仕様。システムGUIDはWindows既存と照合 | **可能** | 名前／数値は設計文書で管理。登録しない |
| T3 | ネイティブTIPとMeltia.exeの隔離ビルド構成。x64/x86のC++ビルド・ABI・プロトコルの単体テスト | **可能なはずだが未検証** | 登録・実機使用はしない。既定Preview OFF/Keyboardを維持 |
| T4 | 検証用VM／隔離Windowsでの登録・解除・入力検証（事前バックアップと復旧）。Meltype既存IMEを壊さない | **ソース更新不要だが実Windows環境が必須** | 実機操作は**専用PR・明示レビュー後**に限る |
| T5 | 本家/Meltia共存、入力消失・重複、IME状態切替、パスワード、低整合性、管理者、編集/候補/確定、長時間E2E | **実機が必須** | 必須ゲート未達ならPreview Keyboard限定 |
| T6 | インストーラー/アンインストーラー/更新/署名/依存物・ライセンス監査、ベータの新しいバージョン・Release | **実装/検証が完了してから** | 署名/本家資産に関する条件を満たすまで配布禁止 |

T1〜T3は**現行上流コードベースのまま**開始できる。ただし「ソースがある」だけではビルドや実機の適合性は成立しない。v1.2.1の修正・v1.2.2以降の修正を先に取り込むかは、実際の失敗再現と依存調査で必要性を決める。

## 4. 実機試験で必須とする受け入れ条件

1. **独立性**：Meltype本家のCOM/TIP GUID/言語プロファイル、更新先、`Program Files\\Meltype`、`%LOCALAPPDATA%\\Meltype`、スタートアップ、辞書/学習ファイルにMeltiaが一切上書き・削除・移行をしない。
2. **安全な入力**：未確定文の原文完全保存、Enter/Space/F6/F7/F10、Backspace/Delete、キャレット/フォーカス移動、キーの再送・修飾・IME切替、文節確定・キャンセル、ロック/URL、かな漢字変換、undo/reconversion、アプリ固有エラー時のfail-closed。
3. **二重入力回避**：フック型Keyboard PreviewとTSFモードを同時有効化せず、同じキーがフック/TSFを二重経路で通らない。旧mutexの変更は安全設計と相互起動テストを経てから。
4. **認可・プロトコル**：C#パイプ名とNative名が一致し、SID・整合性レベル・送信元確認、期限切れ・複数接続・切断時の原文を漏らさない。プロセス名/パスの単純置換で信頼の証明にならないことに注意。
5. **OS別**：Windows 10/11、x64ホストと32bitアプリ（x86 TIP）、UAC/標準権限/昇格、ブラウザー/AppContainer、異なるアプリへの切替・終了/再起動。
6. **復旧可能**：登録前後の状態を検証し、失敗時にMeltia自身の登録のみ巻き戻す。メンテナーの手動「本家のレジストリキー削除」を手順に含めない。
7. **観測**：CIのビルド・静的検証・unit testsと実機試験を明確に分け、合成データと実入力の成功を混同しない。

**T4以降のゲートを満たすまでは** `MELTIA_PREVIEW`の`InputMode.Keyboard`固定、`Updater`非実行、ネイティブTIP未登録を維持する。

## 5. 最初に開ける個別PR（本PRでは未着手）

- **T1-A 契約試験PR（低リスク）**：Meltia Previewで本家TIP起動・更新が行われないことと独立データ先、raw/UTF-16/キー再送契約を再確認。旧CIのWindows hook-free試験と併用する。
- **T1-B C#／C++のIPC仕様書PR（文書・テストのみ）**：`TipServer`/native `Pipe.cpp`のrequest/responseとエラー/fallback/permissionsを対応表へ。
- **T2 識別子独立化設計PR**：生成するMeltia固有GUIDと表示名、実行ファイル/NamedPipe/パスを一ヶ所で管理する設計を審査。既存Meltypeの値と一致しない自動検証を作る。
- **T3 新規Meltia TIP試作PR**：本家`native/tip`は移動/削除せず独立モジュールとしてビルド。**regsvr32やレジストリ操作をCIの通常PRで実行しない**。

## 6. 判断の境界

- **現行バージョンのままで可能：** 上流差分棚卸し、リファクタリング用契約・テスト、独立TSF設計、C#⇔Nativeプロトコル試験、隔離ビルドの試作、Windows Preview Keyboardの回帰。
- **上流更新が必要になる可能性：** 現行TIPの実機で再現する不具合が、上流の特定commitで直っている場合。修正箇所だけを選びR0に記録して移植する。
- **新しいMeltiaバージョン・配布物が必要：** 独立Meltia IMEをテストユーザーへ配布する時。公開済み `1.0.0-beta.1`を再利用・上書きしない。
- **まだ断言できない：** TSFの完全動作、Meltype本家との並行インストール、署名済み安定配布、既存Meltype v1.2.1以降の特定修正が必須かどうか。実機失敗が証拠になってから判断する。
