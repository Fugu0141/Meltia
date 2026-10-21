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
| 実行ガード | 明示的な --vm-readonly と正しい4 GUID | 不正・重複・不足・通常起動ではCOM初期化しない |

**安全上の重要な限界:** GetDisplayAttributeInfoは既存の表示属性プロバイダーをCOMで生成・活性化することがある。TSFに登録やレジストリ書き込みを要求しなくても、間接的な副作用が絶対にないとは証明できない。したがって**復元可能な隔離Windows VM限定**で使う。

API参照: [GetDisplayAttributeInfo](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfdisplayattributemgr-getdisplayattributeinfo) / [EnumItemsInCategory](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcategorymgr-enumitemsincategory) / [表示属性提供](https://learn.microsoft.com/en-us/windows/win32/tsf/providing-display-attributes)。

## 2. 受け入れ条件

1. T2マニフェストからCLSIDと3属性GUIDを引数で指定し、オフラインで形式・重複を検査してからCOM初期化。
2. 属性順・カテゴリ順、B3のキー構造を固定し、既存のT4-B3/B5監査に渡せるJSONを出力。
3. 観測済みの既存属性またはカテゴリ占有があれば終了コード1、すべて陰性または不明でも2。**実登録許可を示す終了コード0は禁止**（合成セルフテストのみ0）。
4. MSVCでx64/x86を別々にビルドし、CIでは --selftest と不正引数拒否テスト**だけ**実行。CIにVM実観測、登録、解除の手順を置かない。
5. T3a登録拒否スタブ、元のnative/tip、Windows Preview、T2識別子、Meltype既存設定は変更しない。

## 3. 隔離Windows VMのオペレーター手順（CIや普段使いのPCでは実行禁止）

1. Windows 10/11隔離VMを用意し、事前にスナップショットを取得する。ビルド環境はWindows SDKとMSVCを備えたx64 Native Tools Command Prompt for VS。
2. 次のコマンドで観測ツールだけをビルドする。Meltia TIP DLLを登録せず、本家のインストーラーや登録スクリプトは使用しない。

~~~cmd
cl /nologo /std:c++17 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE /Fe:MeltiaT4b6VmTsfObserver.exe tools\meltia_t4b6_vm_tsf_observer.cpp /link ole32.lib uuid.lib
MeltiaT4b6VmTsfObserver.exe --selftest
~~~

3. 同じVM内でPowerShellから以下を**明示的に実行**。引数のGUIDは必ずT2の唯一のマニフェストから取得する。

~~~powershell
$m = Get-Content .\config\tsf\meltia-t2-identity.json -Raw | ConvertFrom-Json
& .\MeltiaT4b6VmTsfObserver.exe --vm-readonly $m.identity.clsid $m.identity.inputAttributeGuid $m.identity.convertedAttributeGuid $m.identity.targetAttributeGuid > .\t4-b3-vm-observation.json
$LASTEXITCODE  # 1=占有、2=陰性/不明。いずれも登録不可
python tools/meltia_t4b3_evidence_gate.py --evidence .\t4-b3-vm-observation.json
~~~

PowerShell 5.1のリダイレクト（UTF-16LE BOM）/PowerShell 7のUTF-8は既存のB3コードで処理する。

4. **同じVM・ユーザー・権限・セッション**で取得したT4-A、T4-B1 JSONと合わせ、T4-B5の --attributes 引数にこのJSONを渡して停止理由をレビューする。ログは個人情報を確認し、無断で公開GitHubにアップロードしない。復元試験は別ゲート。

## 4. 次工程の判断

この工程では実登録、逆順rollback、共存、IME実打鍵に到達していない。オープンの親Issue #16は継続。VM実観測後のレビューで不確実性を洗い出し、登録・解除の**専用別PR**（所有権の実証、前後差分、復旧、失敗注入を必須）へ進む。合成CI成功だけで正式IME完成と宣言しない。

## 5. 実施記録

2026-10-21: PR #93のmain統合確認後、T4-B6観測C++実装・x64/x86無COMセルフテスト・CI接続を追加。実VMでのOS観測・登録・キー入力は未実施。
