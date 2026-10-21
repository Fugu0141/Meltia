# T4-B3 — TSF表示属性・カテゴリの証拠契約と衝突検出（2026-10-21）

> **要件定義・基本設計・オフライン実装。** 既存Windows APIを**呼び出さず**、保存済みの観測記録だけを純粋に検証する。実環境のAPI操作、Windowsレジストリ・COM/TSF登録/解除、IME選択、Meltia/MeltypeのDLL配置は本工程では行わない。**観測がすべて「不在」でも登録許可は出さない。**

関連：[Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [T2](MELTIA_T2_TSF_IDENTITY_AND_REGISTRATION_BOUNDARY_20261017.md) / [T4-A](MELTIA_T4A_READONLY_REGISTRATION_PREFLIGHT_20261021.md) / [T4-B1](MELTIA_T4B1_TSF_PROFILE_AUDIT_AND_ROLLBACK_GATES_20261021.md) / [T4-B2](MELTIA_T4B2_OWNERSHIP_AND_ROLLBACK_CONTRACT_20261021.md)。

## 1. まず確認したネイティブ設計と公開API

- 継承元 `native/tip/DisplayAttributes.cpp` の `kSpecs` は、変換前Input / Converted / Targetの3属性について `ITfDisplayAttributeInfo::GetGUID` を実装している。`native/tip/TextService.cpp` の `ITfDisplayAttributeProvider::EnumDisplayAttributeInfo` / `GetDisplayAttributeInfo` が供給する。これらの本家識別子を新しいMeltia属性GUIDに静的に置換しない。
- 継承元 `native/tip/Dll.cpp` のカテゴリ登録は `RegisterCategory(CLSID_TextService, categoryGUID, CLSID_TextService)`。Microsoftの`RegisterCategory`仕様では、第1引数は**所有Text Service CLSID**、第3引数は**登録する項目GUID**。共有カテゴリGUIDそのものは所有対象でない。
- `ITfCategoryMgr::EnumItemsInCategory(category GUID)` はそのカテゴリの項目GUIDを列挙できるが、返るGUIDだけでは第三者の登録時所有者・別スコープ・異なる権限からの完全な可視性を保証しない。
- `ITfDisplayAttributeMgr::GetDisplayAttributeInfo(attribute GUID, &info, &ownerClsid)` は、解決できた場合にproviderのCLSIDを返す。**取得失敗・対象が見えないことはグローバルなGUID不在を証明しない**。属性providerの読み込み・COM活性化等が起こり得るため、将来隔離VMでAPIを直接呼ぶ場合も別審査と実行ログが必要。
- `ITfCategoryMgr::RegisterGUID` は内部テーブルにGUIDを追加するAPIであり、**「読み取り専用プローブ」に使わない**。

参考：Microsoft Learn — [Providing Display Attributes](https://learn.microsoft.com/en-us/windows/win32/tsf/providing-display-attributes)、[RegisterCategory](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcategorymgr-registercategory)、[EnumItemsInCategory](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcategorymgr-enumitemsincategory)、[GetDisplayAttributeInfo](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfdisplayattributemgr-getdisplayattributeinfo)、[RegisterGUID](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcategorymgr-registerguid)。

## 2. 受け入れ条件・安全境界

1. `config/tsf/meltia-t2-identity.json` を唯一のGUIDソースとし、`design-only`・登録無効フラグをT4-Aの契約で再検証する。
2. 3属性 `inputAttributeGuid` / `convertedAttributeGuid` / `targetAttributeGuid` と、5カテゴリ `GUID_TFCAT_...` の順序と値を照合する。対象の欠損・余分・順序違い、UUID不正、CLSID不一致を拒否する。
3. 属性が`resolved`の場合には所有CLSIDが必須。**外国CLSIDは衝突候補、Meltia自身のCLSIDも「新規登録前に既存資源がある」ため停止**。`not-found` / `failed` は常に`inconclusive`に分類し、勝手に不在認定しない。
4. 各カテゴリはMeltia CLSIDの項目が見えるかを合成/観測。列挙が未完なら`present=null`しか許可しない。完全列挙から項目が見えれば`occupied`、項目が見えなくても別ユーザーや登録所有者に関する証明にはならない。
5. JSONのメタ情報は `current-user-visible-tsf-only` に限定し、`registry_writes=false` / `registration_authorized=false` を要求。Windows PowerShell 5.1のUTF-16LE BOM、PowerShell 7のUTF-8 (BOM有無) の保存形式をT4-B1読み取り処理と共有する。
6. **結果は `occupied` (exit1) か `inconclusive` (exit2) のみ**。肯定的な登録許可ステータス・exit0を作らない。結果にも常に `registration_authorized=false` / `native_tsf_access=false` / `automatic_rollback=false` と表示する。
7. 実際のWindows API取得、COM DLL読み込み、TSF登録・解除、レジストリ書き込み、実機入力は行わない。CIは人工的な8項目のJSONだけを試験する。

## 3. 証拠JSONの形（例は**合成観測**でありOS情報ではない）

~~~json
{
  "phase": "T4-B3",
  "coverage": "current-user-visible-tsf-only",
  "registry_writes": false,
  "registration_authorized": false,
  "attributes": [
    {"field": "inputAttributeGuid", "guid": "<T2-manifest-guid>", "lookup": "not-found", "owner_clsid": null},
    {"field": "convertedAttributeGuid", "guid": "<T2-manifest-guid>", "lookup": "not-found", "owner_clsid": null},
    {"field": "targetAttributeGuid", "guid": "<T2-manifest-guid>", "lookup": "not-found", "owner_clsid": null}
  ],
  "categories": [
    {"category": "GUID_TFCAT_TIP_KEYBOARD", "item_clsid": "<T2-manifest-clsid>", "complete": true, "present": false},
    {"category": "GUID_TFCAT_DISPLAYATTRIBUTEPROVIDER", "item_clsid": "<T2-manifest-clsid>", "complete": true, "present": false},
    {"category": "GUID_TFCAT_TIPCAP_INPUTMODECOMPARTMENT", "item_clsid": "<T2-manifest-clsid>", "complete": true, "present": false},
    {"category": "GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT", "item_clsid": "<T2-manifest-clsid>", "complete": true, "present": false},
    {"category": "GUID_TFCAT_TIPCAP_SYSTRAYSUPPORT", "item_clsid": "<T2-manifest-clsid>", "complete": true, "present": false}
  ]
}
~~~

山括弧の欄はマニフェストの実値で置換しない限り検証失敗になる。このJSONは**フォーマット例であり、実VMから採集済みの証拠ではない**。`lookup:not-found`は属性GUIDの永久的な非存在を意味しない。

## 4. ローカル検証

~~~powershell
python tools/test_meltia_t4b3_evidence_gate.py
python tools/meltia_t4b3_evidence_gate.py --evidence <sample-observation.json>
~~~

`tools/test_meltia_t4b3_evidence_gate.py` は8対象の全パターン負例、所有者不一致、TSF属性不在を誤って許可しない回帰、BOM付きJSON、破損/巨大入力を検証。2行目は外部ファイルを**読むのみ**。本段階の正常な陰性記録でもexit2が正しい。

## 5. 次段階の未解決ゲート

- **実観測を取得する安全な手段**：APIが読み取り専用であっても`GetDisplayAttributeInfo`が外部のプロバイダーを起動することを含め、隔離VMに限定し、同意・バックアップ・実行ログ・失敗コードの取り扱いをレビューしてから別PRで実装する。今あるものは合成記録を検証するロジックのみ。
- **カテゴリ所有者の真正性**：`EnumItemsInCategory`から項目の有無が分かっても、登録時の所有者やHKLM/HKCU 32/64bit registry viewの証明は別途必要。証跡と実際のAPIを突き合わせる。
- **ロールバックの証拠**：T4-B2で定義した`created-owned`文字列は実インストール由来ではない。事前/事後の独立スナップショット、ステップごとのreceipt、競合が起きないこと、差分がMeltia専用であることを実証するまで自動削除は行わない。
- **正式IME移行**：OSの安全性・T5実キー入力E2E・T6配布試験が残る。親Issue #16は継続Open。PRマージ後もTSF登録許可にはならない。

## 6. 実施記録

2026-10-21：PR #90 (T4-B2) をmainへSquash merge（`7cd319244636571878505e145c49ff7a6044b5a7`）。T4-B3では公開APIの契約と本家表示属性実装を照合して、**外部証拠が欠ける場合は常に不確定として停止する**オフライン解析器を実装。独立Windows VMでのTSF属性・カテゴリ列挙は未実施。
