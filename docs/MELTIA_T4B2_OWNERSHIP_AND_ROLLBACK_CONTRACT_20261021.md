# T4-B2 — TSF登録所有権・解除対称性・途中失敗時ロールバック契約（2026-10-21）

> **要件定義・基本設計・オフライン模擬試験。** この段階で Windows COM / TSF 登録・解除、IME切替、DLL配置、実レジストリ照会、配布を一切行わない。成功する模擬試験は**実OSでの所有権証明でも登録許可でもない**。

関連：[Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [T2設計マニフェスト](../config/tsf/meltia-t2-identity.json) / [T4-A](MELTIA_T4A_READONLY_REGISTRATION_PREFLIGHT_20261021.md) / [T4-B1](MELTIA_T4B1_TSF_PROFILE_AUDIT_AND_ROLLBACK_GATES_20261021.md)。

## 1. 背景と設計判断

- 継承元 `native/tip/Dll.cpp` の `RegisterComServer` は HKLM `Software\Classes\CLSID\{CLSID}` に `RegCreateKeyExW` を使い、`UnregisterComServer` では `RegDeleteTreeW` により当該サブツリーを削除する。
- 同じファイルの `RegisterProfile` / `UnregisterProfile` は CLSID、言語ID、profile GUID をTSF APIで扱う。`RegisterCategories` は 5つの `(CLSID_TextService, category GUID, CLSID_TextService)` の組を登録/解除する。
- 既存 `DllRegisterServer` の失敗処理は、**その呼出し以前から存在した登録かどうかを検査しない**。登録処理中に失敗しても全カテゴリ・profile・COMを解除しようとする。Meltia用に単純移植すると、既存登録の所有者不明・部分作成・別プロセスによる変更に対応できない。
- T3aで生成した実験DLLの `DllRegisterServer` / `DllUnregisterServer` は拒否スタブであり、T4-B2でも**そのまま残す**。

このため、本工程では**同一マニフェストから作る所有対象の型付き台帳**と、**事前が absent で実際に成功した追加だけを逆順に戻せるかという模擬契約**を作る。レジストリ・TSF API・ファイルの実際の状態を作成/変更しない。

## 2. 受け入れ条件

1. 唯一の識別子入力は `config/tsf/meltia-t2-identity.json`。T2の `design-only`・全deployment無効・既知のMeltype GUID非流用を検証する。categoryリストと本家TIPの5つのカテゴリの静的対応を保持する。
2. COMのx64/x86 registry viewを**別々の抽象対象**として台帳化。継承元がHKLMを使用する点を記述するが、実Windows viewへの登録・キー列挙・所有権判定を**主張しない**。
3. 言語profileは `(Meltia CLSID, LANGID 0x0411, profile GUID)` の3点、カテゴリは `(Meltia CLSID, category GUID, Meltia CLSID)` の5組に限定する。Windowsが共有するカテゴリGUID自体、別製品のカテゴリ組、Meltype本家のCOM/TIPは**所有対象に含めない**。
4. 3種類のdisplay attribute GUIDは必要な所有権調査が未完であることを台帳に明記。**現時点で削除対象にしない**。属性GUIDとその利用箇所・TSF登録所有関係の実環境調査は後続ゲートとする。
5. シミュレーションでは全対象の事前状態 `absent` を条件に、登録順の先頭N件だけが `created-owned` だったと仮定し、**逆順**のrollback候補のみを生成する。N=0〜8の全パターンをテスト。
6. 事前が既存/不明、スナップショット欠損・余分な対象、順序違反・二重適用、外部変更・実行後の不明な状態では**停止**し、削除候補を出さない。
7. テストが何件成功しても `registration_authorized=false`、`registry_writes=false`、`automatic_rollback=false`。実際の登録・解除を行うAPIとPowerShell操作を追加しない。既存PreviewとMeltypeの入力・更新・設定・辞書・Hook処理は変えない。

## 3. 所有権台帳の対象（**設計値**）

| 登録単位 | 抽象対象 | 所有者・解除の境界 |
| --- | --- | --- |
| COM 64bit | `com:64` | HKLM CLSID サブツリー／64bit view。所有証拠が必要。 |
| COM 32bit | `com:32` | HKLM CLSID サブツリー／32bit view。64bitと独立に観測。 |
| TSF日本語profile | `profile` | `(Meltia CLSID, 0x0411, Meltia profile GUID)` と完全一致したprofileだけ。 |
| TSFカテゴリ関連付け | `category:GUID_TFCAT_...` 5項目 | `(Meltia CLSID, 指定カテゴリGUID, Meltia CLSID)` の完全一致組だけ。共有カテゴリ定義自体は残す。 |
| Display属性の3 GUID | 台帳では **unverified** | 本工程では登録資源とみなさず、削除不可。 |
| 配置ファイル (x64/x86) | **対象未確定** | T2にはインストールディレクトリのルートだけ存在する。x86/x64サブディレクトリとハッシュ・署名・設置時の証跡が固まるまでは削除計画を立てない。 |

`tools/meltia_t4b2_ownership_model.py` が型付き台帳と合成rollback結果をJSON標準出力に出す。

## 4. 「rollback候補」の意味と限界

模擬的な `created-owned` という文字列は**ユーザーが入力した、または生成した合成事実**であり、Windowsの所有権を証明しない。たとえすべての入力が `absent`→`created-owned` で、`simulation-reversible-not-authorized` を返しても、**実OSの登録・削除・自動復旧は一切許可されない**。

実登録・解除に進むには、少なくとも以下を別途解決する。

1. **事前の独立観測**：隔離VMで各scope/view/ユーザーから、COM・TSF profile・カテゴリ関連付けの全対象が事前に不在/占有/確認不可のどれかを記録。占有・確認不可なら無条件停止。
2. **所有証跡と差分**：Meltia専用のインストールセッションで追加した個別資源だけを特定し、登録時の成功/失敗とバイナリハッシュを記録。既存キーの値の上書きや共有カテゴリ定義への変更をしない。
3. **後状態の独立照会**：COM HKLM/HKCU と両view、TSF profile（ユーザー/言語）、カテゴリ関連付け、3表示属性の実OS利用状況を確認。APIが属性GUIDをどう所有・公開するかはまだ未確定。
4. **途中失敗と競合**：別プロセスの干渉・アクセス拒否・登録直後の部分障害・解除失敗を注入し、所有確認のできる変更だけ逆順に復旧できることを隔離VMで実証。異常時に自動で `RegDeleteTree` を行う方式は採用しない。
5. **対象の同居・再起動**：Meltype本家と共存し、他ユーザー・別権限・x86アプリ・再起動後の復旧も含めて確認する。実VM試験と人手レビューの完了前に本番環境でTSFを登録しない。

## 5. 実行・テスト

~~~powershell
python tools/test_meltia_t4b2_ownership_model.py
python tools/meltia_t4b2_ownership_model.py --simulate-prefix 4
~~~

2行目は `com:64`, `com:32`, `profile`, 最初のcategoryまで**模擬追加**したと仮定する。返る `candidate_rollback_order` は最後に追加したcategory→profile→COM 32bit→COM 64bitという**文字列の順番**だけで、Windowsでは何も実行しない。ユーザーのレジストリやプロファイル一覧にはアクセスしない。

CIはPythonの合成テストだけを実施。T4-B1のTSF列挙はCIでは `--selftest` に限定したまま、T3a登録拒否スタブ、T3c〜T3g、Core/Preview既定経路は維持する。

## 6. 次段階（T4-B3）へ進む条件

- T4-B2で規定した設計対象と実際のTSF API登録オブジェクトの対応、display attributeの所有関係、ファイル配置がレビューされている。
- 実行可能な登録/解除コードを導入する場合は**さらに別PR**・隔離VMスナップショット・明示的な承認・独立した登録前検査・フェイルクローズの復旧設計を必須とする。
- **CIでの実登録、Meltype本家のGUID/設定/ファイル操作、製品配布は引き続き禁止**。親Issue #16は未完了のまま。

## 7. 実施記録

- 2026-10-21：PR #89 (T4-B1) mainへSquash merge `0e9c9c69472df807fc6d8f06575d9d2a548ab508`。T4-B2では設計台帳・模擬rollbackと負例テストのみ新設。Windows VM登録・解除・TSF実属性列挙・実打鍵は**実施していない**。
