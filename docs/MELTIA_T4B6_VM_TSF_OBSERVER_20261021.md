# T4-B6 — 隔離VM用TSF実観測器（2026-10-21）

> **要件・基本設計・無登録実装。** WindowsのTSF APIで表示属性3件・カテゴリ5件を実際に観測し、T4-B3形式のJSONを生成する。CIではCOM/TSF APIを呼ばない。**IME登録・解除・実打鍵はしない。**

関連: [Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [T4-B3](MELTIA_T4B3_ATTRIBUTE_CATEGORY_EVIDENCE_CONTRACT_20261021.md) / [T4-B5](MELTIA_T4B5_OFFLINE_EVIDENCE_RECONCILIATION_20261021.md) / [T2識別子](../config/tsf/meltia-t2-identity.json)。

## 1. 背景と範囲

従来T4-B5は合成・外部JSONを検証したが、T4-B3表示属性／カテゴリ情報を実Windowsから得る収集器がなかった。今回、観測器と拒否型の検証器を明確に分ける。

| 観測対象 | 方式 | 不確実性の扱い |
| --- | --- | --- |
| 表示属性3件 | ITfDisplayAttributeMgr::GetDisplayAttributeInfo | S_OKかつオブジェクト取得時だけresolved、他はすべてfailed（不在とは断定しない） |
| カテゴリ5件 | ITfCategoryMgr::EnumItemsInCategory | 10万件の上限、失敗時はcomplete=falseとpresent=null。完全列挙時だけ真偽を出す |
| 出力 | T4-B3の厳密な8対象JSON | 個人のパス・ユーザー名・他のIME名を出さず、対象と属性所有CLSIDのみ |
| 実行ガード | T2全体を検証した一時ヘッダーと実行時マニフェストの完全一致、役割順GUIDの一致 | 不正・変更・重複・不足・通常起動ではCOM初期化しない |

**安全上の重要な限界:** GetDisplayAttributeInfoは既存の表示属性プロバイダーをCOMで生成・活性化することがある。TSFに登録やレジストリ書き込みを要求しなくても、間接的な副作用が絶対にないとは証明できない。したがって**復元可能な隔離Windows VM限定**で使う。

API参照: [GetDisplayAttributeInfo](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfdisplayattributemgr-getdisplayattributeinfo) / [EnumItemsInCategory](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcategorymgr-enumitemsincategory) / [表示属性提供](https://learn.microsoft.com/en-us/windows/win32/tsf/providing-display-attributes)。

## 2. 受け入れ条件

1. T2マニフェストのdesign-only・registrationEnabled=false・deployment全falseを既存T2バリデータで検証した後、一時ヘッダーを生成。実行時は指定T2ファイルをこのビルド時スナップショットと**バイト単位で完全一致**確認し、CLSIDと3属性GUIDの**役割・位置を含む完全一致**確認をしてからCOM初期化。任意の有効GUID4件は拒否。スナップショットはリポジトリにコミットしない。
2. 属性順・カテゴリ順、B3のキー構造を固定し、既存のT4-B3/B5監査に渡せるJSONを出力。
3. 観測済みの既存属性またはカテゴリ占有があれば終了コード1、すべて陰性または不明でも2。**実登録許可を示す終了コード0は禁止**（合成セルフテストのみ0）。
4. MSVCでx64/x86を別々にビルドし、CIでは --selftest と **--selftest-guard**（正しいT2／属性順入替／無関係GUID／改変・欠損マニフェスト／不正引数）だけ実行。これらはCOMを起動しない。CIに --vm-readonly、VM実観測、登録、解除の手順を置かない。カテゴリ列挙途中失敗／件数上限、表示属性失敗分岐も純粋関数で合成テストする。
5. T3a登録拒否スタブ、元のnative/tip、Windows Preview、T2識別子、Meltype既存設定は変更しない。

## 3. 隔離Windows VMのオペレーター手順（CIや普段使いのPCでは実行禁止）

1. Windows 10/11隔離VMを用意し、事前にスナップショットを取得する。ビルド環境はWindows SDKとMSVCを備えたx64 Native Tools Command Prompt for VS。
2. 次のコマンドで観測ツールだけをビルドする。Meltia TIP DLLを登録せず、本家のインストーラーや登録スクリプトは使用しない。

~~~cmd
set "T4B6_STAGE=%TEMP%\meltia-t4b6-vm-review"
mkdir "%T4B6_STAGE%"
python tools\generate_meltia_t4b6_snapshot.py --output "%T4B6_STAGE%\MeltiaT4b6IdentitySnapshot.h"
cl /nologo /std:c++17 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE /I"%T4B6_STAGE%" /Fe:"%T4B6_STAGE%\MeltiaT4b6VmTsfObserver.exe" tools\meltia_t4b6_vm_tsf_observer.cpp /link ole32.lib uuid.lib
"%T4B6_STAGE%\MeltiaT4b6VmTsfObserver.exe" --selftest
~~~

3. 同じVM内でPowerShellから以下を**明示的に実行**。引数のGUIDは必ずT2の唯一のマニフェストから取得する。

~~~powershell
$m = Get-Content .\config\tsf\meltia-t2-identity.json -Raw | ConvertFrom-Json
& "$env:TEMP\meltia-t4b6-vm-review\MeltiaT4b6VmTsfObserver.exe" --vm-readonly .\config\tsf\meltia-t2-identity.json ($m.identity.clsid) ($m.identity.inputAttributeGuid) ($m.identity.convertedAttributeGuid) ($m.identity.targetAttributeGuid) > .\t4-b3-vm-observation.json
$LASTEXITCODE  # 1=占有、2=陰性/不明。いずれも登録不可
python tools/meltia_t4b3_evidence_gate.py --evidence .\t4-b3-vm-observation.json
~~~

ビルド後にT2マニフェストを変更した場合は、ツールはCOM起動前に拒否する。再生成・再ビルドしてもT2が登録ONであれば生成器が停止する。PowerShell 5.1のリダイレクト（UTF-16LE BOM）/PowerShell 7のUTF-8は既存のB3コードで処理する。`--selftest-guard` は**同じ事前検査だけを行う合成試験用**で、成功しても実機登録許可を意味しない。

4. **同じVM・ユーザー・権限・セッション**で取得したT4-A、T4-B1 JSONと合わせ、T4-B5の --attributes 引数にこのJSONを渡して停止理由をレビューする。ログは個人情報を確認し、無断で公開GitHubにアップロードしない。復元試験は別ゲート。

## 4. 次工程の判断

この工程では実登録、逆順rollback、共存、IME実打鍵に到達していない。オープンの親Issue #16は継続。VM実観測後のレビューで不確実性を洗い出し、登録・解除の**専用別PR**（所有権の実証、前後差分、復旧、失敗注入を必須）へ進む。合成CI成功だけで正式IME完成と宣言しない。

## 5. 実施記録

2026-10-21: PR #93のmain統合確認後、T4-B6観測C++実装・x64/x86無COMセルフテスト・CI接続を追加。実VMでのOS観測・登録・キー入力は未実施。

## 6. PR #94 レビュー P2/P3 対応（2026-10-21）

- **P2（必須）**: GUID形式・重複のみの検査を廃止。T2全契約を検証済みの一時C++ヘッダーへ固定し、実行時にmanifestのバイト一致と役割順4 GUID一致をCOMの前段で確認。どちらが欠けてもコード2で停止。単にオペレーターの指定を信用しない。生成器・ネイティブ双方の拒否試験をCIへ追加。
- **P3（改善）**: COMを起動しない `ApplyCategoryPage` と `ClassifyAttribute` の合成検証を導入。途中失敗、既知占有の途中失敗、EOF、10万件上限、属性のHRESULT/NULLを検証。
- **限界**: 生成器によるT2合格は、外部のVM真正性、他ユーザーのGUID不在、COMプロバイダーの無副作用、TSF登録許可、rollbackを証明しない。隔離VM専用の手順とT4-B5の停止判定は継続する。
