# T2 — Meltia独立Windows TSF識別子・登録境界の要件定義／基本設計（2026-10-17）

> **このフェーズは設計・マニフェスト・静的検査のみ。TSF登録、COM DLL生成、regsvr32、HKLM/HKCUの変更、WindowsのIME切替、NativeとC#の実通信は一切行わない。** 本家Meltype用の既存スクリプトをMeltia用に流用しない。発行したUUIDは**設計候補**であり、Windowsマシン上の全登録CLSIDとの衝突検査はT4の隔離VMで別途必要。

基点：main `f8dfe4aa6aa73fee5a69bfa35482667057fba163`。前段：[独立IMEゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)／[T1-B C#↔native IPC](MELTIA_T1B_TIP_IPC_PROTOCOL_CONTRACT_20261017.md)。上位Issue [#16](https://github.com/Fugu0141/Meltia/issues/16)。継承した`native/tip`と`src/Meltype/Tip/TipServer.cs`は**依然としてMeltype専用**。

## 1. T2の範囲と要件

1. **分離する対象**：WindowsのCOM CLSID、TSF language profile GUID、3種のTSF表示属性GUID、IME表示名、ユーザーSIDに結びつくPipe名、IPCバージョン、配布・ログ・設定パス、登録/解除が操作可能な対象。
2. **変更しないもの**：元の`native/tip/Globals.h`や`Pipe.cpp`、`Dll.cpp`、`Register-Tip.ps1`、`src/Meltype/Tip/TipServer.cs`、Windows Keyboard Previewの既定`InputMode.Keyboard`。これらの本家GUID/処理を一斉置換しない。
3. **公開/実行しない**：独立IMEとしての起動・登録・署名・配布、Windowsの入力方式切替。本家アプリの既存登録・設定・更新先・辞書に触れない。
4. **T3への引渡し**：マニフェスト上の5つの固有GUIDと通信名前空間を**同じ定数生成元**からC++／C#／登録マニフェストへ反映し、無フック・無登録のネイティブビルド/解析を優先する。コード別のコピペ定義を許さない。

## 2. 採番済み・固定予定の識別子

source-of-truth: [`config/tsf/meltia-t2-identity.json`](../config/tsf/meltia-t2-identity.json)。UUIDv4を生成し、**`native/tip/*.h`・`native/tip/*.cpp`・`native/tip/*.ps1`・`src/Meltype/Tip/*.cs`に書かれたリテラルGUID**と重複しないことだけをオフラインで検査する。**リポジトリ全域のGUID検査、ヘッダーに名前だけ出てくるWindows OS共有カテゴリGUIDの数値照合、Windowsレジストリに登録された全CLSID/プロファイルとの衝突確認は行っていない。** 実OSの登録検証はT4で行う。

| 用途 | Meltia設計候補 GUID | 本家で使われる値 |
| --- | --- | --- |
| COM CLSID / text service | `D57FA595-DDBD-442A-AD59-AA5142456D64` | `417D801B-A9BD-4C26-BD16-356A825A6998` |
| 日本語language profile | `E2357811-7199-43B7-829B-CA1C784E39B8` | `21F643F4-72D5-4946-BF70-0A126AB52D05` |
| 変換中Input属性 | `5C575D03-BED4-49A1-991D-9138C8714DA4` | `C8107D36-2806-4D8F-A183-FD0AEB8ED862` |
| 変換済みConverted属性 | `831A603D-F822-4809-885E-4C0BC707761A` | `E17AA399-C5EB-42E6-BE45-C44F0651F2E2` |
| 選択中Target属性 | `CCA9BC86-E9FD-4E89-B7E7-2E9275045AA9` | `CEF94813-C47F-48C1-B4EF-D4582D5D4054` |

`LANGID=0x0411`（日本語）はOSが既に定義する地域識別であり、**変更/独占するGUIDではない**。OS定義のTSF categoryや`ITfInputProcessorProfileMgr`のCOM GUIDもそのまま利用する。本家からコピーすべきではないものと、Windows OSが共有する識別子を区別する。

## 3. IPCとプロトコル

- Pipe名：`\\.\pipe\Meltia.Tip.v1.<user SID>` （設計）。本家`Meltype.Tip.<SID>`とは独立。
- 新しいC# server、Native TIP両方で`version: 1`が合うことを**状態開始前に**確認するhandshakeをT3で実装。T1-Bの`hello/key/commit/select/close`、`sid`、`Consumed/Commits/View`の意味を守る。
- バージョン不一致/未解釈コマンド、タイムアウト/切断では**未処理入力に変換/削除/再送ループを発生させない**。Nativeの実際のfail-closed動作はT4〜T5でE2E。
- SIDを末尾へ付ける設計が正しいか、OSによる名前名前空間・Pipe access policy、サーバー・クライアント間の検証は**T3から別途評価**。現段階では本家の通信を動かさない。

## 4. DLL・パス・登録ライフサイクルの境界

| 対象 | Meltiaの設計値／作業 | 制約 |
| --- | --- | --- |
| Native成果物 | `MeltiaTip.dll` (x86/x64 の別ビルド) | T3は隔離ビルドまで。実登録/配布はしない |
| インストール先 | `%ProgramFiles%\Meltia\tip`（x86配備経路を別途確認） | `C:\Program Files\Meltype\tip` を一切更新しない |
| 学習/設定 | `%LOCALAPPDATA%\Meltia` | 本家の`Meltype`以下を読込・移行・削除しない |
| ログ | `%LOCALAPPDATA%\Meltia\logs` | キー入力本文をログに出さない |
| COM/TIP登録 | MeltiaのCLSID/プロファイル/属性と**Meltia CLSIDを所有者とするTSFカテゴリ関連付け** | T4隔離VMでdry-run差分→明示レビュー→登録。OS共有カテゴリ定義や他製品の登録は変更しない |
| アンインストール | **MeltiaのCLSID、言語プロファイル、Meltia CLSIDに紐づくTSFカテゴリ関連付け、Meltia専用設置ファイル** | 設置時の所有マニフェストで厳密に照合。**Windows共通カテゴリのGUID定義、Meltype/別製品の関連付け・登録・ファイル、ユーザーデータ/ログを自動削除しない** |
| Windows Preview | `MELTIA_PREVIEW`、Keyboardのみ | T3/T4に至るまで常にそのまま |

※ 正式な登録に必要なレジストリキー/COMクラス/属性プロバイダー/カテゴリーは`native/tip/Dll.cpp`の`DllRegisterServer`、`RegisterProfile`、`RegisterCategories`を調査してT3で**新しい実装構成へ明示的に移す**。名称変更のみで本家DLLの実行・上書きは不可。

### TSFカテゴリとアンインストールの所有権（PR #78レビューP2）

継承元`native/tip/Dll.cpp`では、`RegisterCategories(true)`が`RegisterCategory(CLSID_TextService, categoryGUID, CLSID_TextService)`を呼び、解除は`RegisterCategories(false)`で`UnregisterCategory(CLSID_TextService, categoryGUID, CLSID_TextService)`を呼ぶ。T3のMeltia専用実装も**登録されたペアの所有者がMeltia CLSIDである場合に限って**対称に解除する。Windowsが共有するカテゴリ定義のGUID自体は解除・削除しない。

T2マニフェストの`distribution.uninstallOwnership`には、COM CLSID、言語プロファイル、カテゴリ所有者CLSID、対象カテゴリリスト、設置ファイルのルート、共有カテゴリ・他製品登録・ユーザーデータを削除しないフラグを記録。対象カテゴリは`GUID_TFCAT_TIP_KEYBOARD`、`GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER`、`GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT`、`GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT`、`GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT`。T2の静的テストは`Dll.cpp`の`kCategories`とマニフェストの一致を確認する。

将来T3で追加/削除するカテゴリがある場合、登録と解除の両方向、属性GUIDの登録所有権、失敗時ロールバックを**同時に設計レビュー**する。設置ファイルはMeltiaが配置した物だけ削除し、本家ディレクトリやWindows共通TSFキーをディレクトリごと消す動作は許可しない。上記の設計は**登録・削除コードの実行や実機検証を意味しない**。


## 5. T3へ進む前の契約・検証

```sh
python tools/test_meltia_t2_identity_contract.py
python tools/test_meltia_tip_ipc_contract.py
python tools/test_meltia_keyboard_preview_isolation.py
```

`test_meltia_t2_identity_contract.py`は標準ライブラリのみで実行する**9テスト**。5 GUIDの正規UUIDv4性・重複/対象ファイル内の本家GUIDとの衝突・Meltia固有Pipe・SID・バージョン・無効化フラグ・配布先・カテゴリ関連付け所有権を確認し、重複GUID/本家GUID/登録有効化/名前誤り/カテゴリ解除対象や他製品削除フラグを故意に混ぜて**テストが拒否すること**も検査する。**無登録・静的設計テスト**でありOSとの共存・通信成功を保証しない。

T3以降の必須チェック：

1. 同じマニフェストをもとにC++定数・C#定数・登録/解除マニフェストを生成、異なるGUIDを手入力しない。独立`MeltiaTip`というソース/バイナリ境界を持つ。
2. C++ x86/x64ビルド、C# response parser/契約、エラー時のパススルー、Nativeのfixture readerを構築。**接続しないテストから始める**。
3. T4の隔離Windows VMにて、登録前後のCOM/TSFスナップショットと共存チェック、インストール・アンインストール/復旧方法を定義。Windows実機のGUID衝突はここで検証。
4. T5の実キー入力E2E・負荷/長文/フォーカス/アプリ別差の合格前にReleaseしない。

**T2完了は設計と静的検証が整ったことを意味するだけで、MeltiaがWindowsのIME一覧に出ることを意味しない。**

## PR #78 レビュー対応（2026-10-17）

- **P2**：マニフェストの`uninstallScope`をCLSIDとprofileだけでなく、Meltia CLSIDに所属するTSFカテゴリ関連付け・Meltia専用設置ファイルまで明記。`uninstallOwnership`で所有範囲・削除禁止対象を構造化し、元の`Dll.cpp`のカテゴリ配列との照合と改変負例を追加。
- **P3**：GUID衝突検査の対象を`native/tip`のC++ソース・ヘッダー・PowerShellと`src/Meltype/Tip`のC#に拡大し、**それ以外のファイル・OS実機のレジストリ・Windows SDKのカテゴリ定義は未確認**と限定した。リポジトリ全件や実OS全件の保証は主張しない。
