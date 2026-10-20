# T4-A — Windows TSF登録前の読み取り専用インベントリとVM実施ゲート（2026-10-21）

> **要件定義・基本設計・登録前preflightのみ。** 本工程ではCOM/TSF登録・解除、DLL実行、`regsvr32`、レジストリ更新、インストール、IME切替、実打鍵、配布を行わない。T3gまでの無登録IPC成果を、T4の隔離VMで実際に登録してよいという意味にはしない。

関連：[親Issue #16](https://github.com/Fugu0141/Meltia/issues/16)、[T2識別子と所有権](MELTIA_T2_TSF_IDENTITY_AND_REGISTRATION_BOUNDARY_20261017.md)、[T3a無登録ビルド](MELTIA_T3A_ISOLATED_NATIVE_TIP_BUILD_20261017.md)、[T3g所有worker回収](MELTIA_R2_T3G_SUPERVISOR_JOB_CONTAINMENT_20261020.md)、[T4/T5基準](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 1. 背景

T3a〜T3gは隔離x64/x86ビルド、Native↔C#通信、offline host起動/終了/回収を確認している。ただし**生成したDLLは登録を意図的に拒否する**。現時点では正式Meltia TSF用の登録実装、導入・ロールバック実装、隔離VM上の共存実証はない。

T2で生成した5つのGUIDがソース上で本家と重複しなくても、**各Windows環境の既存登録との衝突は未確認**である。T4を急いで登録するのではなく、読み取り専用の事前チェックとT4-Bのレビューゲートを確立する。

## 2. T4-A受け入れ条件

1. `config/tsf/meltia-t2-identity.json`を唯一のGUID入力とし、T2のdesign-only/registration-disabled契約を再検証する。値の直接コピー・新GUID再採番はしない。
2. Windowsホスト上でHKLM/HKCUの**COM CLSIDおよびCTF TIPの候補登録キー**について、32bit・64bit registry viewを読み取る。既存キーを発見すれば**衝突/占有の可能性**として停止し、アクセス拒否・ビュー非対応その他の失敗は**不明**として停止する。値の読み取り・ユーザー識別情報収集は行わない。
3. `tools/meltia_t4a_preflight.py`は登録を行うAPI、レジストリの書込みAPI、サブプロセス起動、ファイル書込みを持たず、標準出力に結果JSONのみ出す。Windows以外は成功扱いにしない。
4. ユニットテストではレジストリ照会を差し替え、**不在／既存キー／アクセス不明／T2フラグ変更**を検証する。通常CIで実Windows登録状態の合格は主張しない。
5. Windows隔離VM用の前提・バックアップ・観測項目・中断条件・手動復旧計画をMarkdown化する。いずれも**実施済み扱いしない**。
6. 既存Keyboard Previewや本家Meltypeの設定・ビルド・登録・更新・原文/変換挙動を変更しない。

## 3. 静的調査と実装境界

- 既存`native/tip/Dll.cpp`は本家用`RegisterComServer`/`RegisterProfile`/`RegisterCategories`と解除処理を持つ。**移植・呼出しはしない**。
- `tools/prepare_meltia_tip_stage.py`はT3aで生成した`DllRegisterServer`/`DllUnregisterServer`を拒否スタブ化する。T4-Aで解除しない。
- T2マニフェストの`uninstallOwnership`にある5カテゴリはMeltia CLSIDに従属する登録対であり、OSが共有するカテゴリGUID自体の削除権限を意味しない。
- preflightが照会するのは**40個の固定された候補パスが存在するかどうか**だけ（5 GUID × HKLM/HKCU × 32/64bit view × 2種の親キーパターン）。パターンは `Software\\Classes\\CLSID\\{GUID}` と `Software\\Microsoft\\CTF\\TIP\\{GUID}`。この40件は「5つのGUIDそれぞれが本来登録される場所」を表すものでは**ない**。属性GUIDやProfile GUIDでも上記の**CLSID/TIP親キー位置を機械的に探索するだけ**であり、登録先の完全な検証ではない。
- **特にTSF language profile:** 通常はText ServiceのCLSID・言語IDに紐づくprofile GUIDとして登録される。別のTIPの配下で同じprofile GUIDが使われていても、単純な `TIP\\{profileGuid}` の照会は見つけられない。Input/Converted/Target属性GUIDも、COM CLSIDやTIPの親キーに存在するとは限らず、**属性の登録状況や共有衝突を証明できない**。現状の40件はCLSID候補についての簡易的な占有兆候と、それ以外の候補についての限定的な異常検知である。
- 正式な**Profile GUID／属性GUIDの衝突調査**はT4-Bの独立ゲートで、TSFの公開APIを用いたプロファイル列挙・属性GUIDの登録／所有関係・インストール対象の対称性を検証し、別レビューする。T4-Aの `precheck-clear` は「40パスが観測範囲で見当たらなかった」の意味に限り、GUIDの未使用、共存安全、TSF登録許可とは扱わない。参照：[Microsoft TSF Text Service Registration](https://learn.microsoft.com/en-us/windows/win32/tsf/text-service-registration)、[ITfInputProcessorProfileMgr::RegisterProfile](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-registerprofile)。
- **非対象:** COM API上の全TSF profile列挙、属性の登録所有者調査、すべてのOS共有GUID、プロセス内キャッシュ、別マシン、実レジストリACL、インストール先のファイル競合、ユーザー別の入力方式一覧。preflightの`precheck-clear`は**未登録/安全/導入許可の証明ではない**。

## 4. 実行方法と判定

```powershell
python tools/test_meltia_t4a_preflight.py
python tools/meltia_t4a_preflight.py
```

後者は **隔離したWindows VM上で、登録より前に人が実行**する。結果はstdoutのJSONだけ。exit 0は「上記40パスのキーが全て不在、照会成功」、exit 1は「既存キーあり」、exit 2は「環境/照会/T2契約が未確認」であり、**どの場合も登録の自動開始はしない**。外部ファイルへの保存は作業者が明示的に行い、個人情報を含むログを公開しない。

| 判定 | 次の操作 |
| --- | --- |
| `precheck-clear` | 手順と観測をレビューし、**T4-Bの別PR**で登録実験の承認を待つ |
| `occupied` | 変更を行わず停止。候補GUIDと既存占有者の特定を隔離VM内で別途調査 |
| `inconclusive` | 読み取り権限・OS構成・registry viewを確認するまで停止 |

## 5. 将来のT4-B隔離VM用チェックリスト（未実施）

1. **専用VM**：Windows 10/11のサポート範囲・x64 OSと32bitアプリ・復元可能なスナップショットを用意。本家Meltypeが入っている場合、IME一覧と関連情報を事前保存。
2. **変更前記録**：OSビルド、ユーザー/管理者境界、Meltia予定CLSID/Profile/カテゴリの存在、登録先DLLハッシュ、MeltypeのCOM/TSF状態、インストール先、CTF profile一覧を取得（必要な情報のみ）。このpreflightはその一部しか収集しない。
3. **所有権設計レビュー**：Meltia用COM CLSID、profile、属性、Meltia CLSIDを所有者とする5カテゴリの登録対を**作成/削除対称**で確認。HKLM/HKCU、x86/x64の場所を仕様化してからAPIを選択。
4. **失敗時のrollback**：途中で失敗した場合、新たに作ったMeltia専用キー/ペア/配置ファイル**だけ**を削除し、事前に存在した項目は変更しない。VMスナップショットを最終復旧手段とする。本家Meltypeや共有カテゴリは削除しない。
5. **中断条件**：衝突/不明、既定Preview経路への接続、同一SIDの既存プロセス干渉、登録状態の取り違い、復旧未検証のときは登録しない。
6. **別レビュー後**：T4-Bで登録・解除を隔離VM上で実験する場合のみ明示的に許可された手順を使い、T5の実打鍵・入力消失防止へ進む。CIでは自動登録をしない。

## 6. 検証記録（2026-10-21時点）

- T3g [PR #87](https://github.com/Fugu0141/Meltia/pull/87)はmainへマージ済み。T3g Windows Preview CI run [39448576638](https://github.com/Fugu0141/Meltia/actions/runs/39448576638) とStrict Quality run [39448576677](https://github.com/Fugu0141/Meltia/actions/runs/39448576677)はsuccess。
- T4-AのVM registry照会・Windows 10/11実機TSF登録/解除・実打鍵は**未実施**。PRのユニットテスト結果と人手VM確認を混同しない。
- PR #88レビューP3対応：40パスの存在確認と「5 GUIDの実登録位置での衝突確認」を区別。実`winreg`にアクセスしない`winreg`モジュール差し替え試験で`KEY_READ`+WOW64のビュー指定、キー不在、アクセス拒否/OSエラーの扱いを検証する。**合格したCI run / Windows VM実測はここには未記録**（修正HEADのActions結果確認後に追記）。
