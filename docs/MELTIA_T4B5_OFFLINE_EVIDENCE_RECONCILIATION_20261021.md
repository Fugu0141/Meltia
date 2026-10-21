# T4-B5 — T4登録前証拠の統合・fail-closedレビューゲート（2026-10-21）

> **要件定義・基本設計・無登録のオフライン実装。** 既存T4-A/B1/B3の外部JSONと、T3a無登録ステージ内のT4-B4成果物を照合するだけ。Windowsレジストリ、COM、TSF API、Native DLLの実行、登録・解除、IME選択、実キー入力、インストール、配布は**行わない**。どんな入力でも登録を許可する結果・exit 0は返さない。

関連：親[Issue #16](https://github.com/Fugu0141/Meltia/issues/16)、[T2](MELTIA_T2_TSF_IDENTITY_AND_REGISTRATION_BOUNDARY_20261017.md)、[T4-A](MELTIA_T4A_READONLY_REGISTRATION_PREFLIGHT_20261021.md)、[T4-B1](MELTIA_T4B1_TSF_PROFILE_AUDIT_AND_ROLLBACK_GATES_20261021.md)、[T4-B2](MELTIA_T4B2_OWNERSHIP_AND_ROLLBACK_CONTRACT_20261021.md)、[T4-B3](MELTIA_T4B3_ATTRIBUTE_CATEGORY_EVIDENCE_CONTRACT_20261021.md)、[T4-B4](MELTIA_T4B4_NATIVE_ARTIFACT_AUDIT_20261021.md)。

## 1. 背景と作業範囲

T4-Aは候補キー40件、T4-B1は可視TSFプロファイル、T4-B2は所有権の**合成モデル**、T4-B3は属性3件とカテゴリ5件の**保存済み観測JSON**、T4-B4はT3aのx64/x86 PEヘッダー・サイズ・SHA-256をそれぞれ扱う。個別の検査が正常終了しても**同一VM、同一ユーザー／権限、同一時点・セッション、DLLの生成由来、登録・逆順解除の所有証拠が揃ったとは限らない**。

T4-B5は登録を進めるためのスイッチではなく、レビュー時に未解決事項を見落とさないための**停止専用ゲート**である。T3aの登録拒否スタブも継続し、実OSの登録を試みない。

## 2. 要件・受け入れ条件

1. GUIDの唯一の真実源は設計専用 `config/tsf/meltia-t2-identity.json`。T2の全deploymentフラグ無効、T4-B2の8登録対象・3未証明属性、T4-B4の無登録成果物制約を確認する。将来の登録ONマニフェストを本ゲートに流用しない。
2. T4-A（40候補レジストリキー）+T4-B1（全言語の現ユーザー可視プロファイル）の組は既存 `meltia_t4b_audit_gate.assess` を再利用する。件数、対象ID、順序、scope、phase不一致は不確定で停止。
3. T4-B3の属性GUID3件／カテゴリ関連付け5件は既存 `meltia_t4b3_evidence_gate` に従う。見える既存資源が1件でもあれば `blocked-occupied`。未検出や失敗は安全の証明ではない。
4. B4の保存済み結果だけを信用せず、指定された**既存**T3aステージ内の両DLLを読み取り専用で再検査する。PE構造やSHA-256の存在は、DLLの生成由来・登録スタブの実行検証・コード署名の代わりではない。
5. JSON入力は131072 byte以下・非シンボリックリンクのファイルに限定。PowerShell 5.1 UTF-16 BOM／PowerShell 7 UTF-8 BOM有無を受容し、重複JSONキー・破損・過大・欠損を拒否。ファイルパス、ユーザー名、SID等は標準出力に転記しない。
6. 判定は必ず `blocked-occupied` または `blocked-inconclusive`、戻り値はそれぞれ1/2。**exit 0を返す経路、`registration_authorized=true`、実レジストリ変更、COM/TSF呼び出し、DLLロード、自動ロールバック、配布機能を禁止**。
7. 既存Preview、辞書、境界判定、TipServer/Native本家実装、T3a〜T3gの経路は不変更。CIは人工的なPE・TSF/registry証拠を使う純粋テストだけで、実VM観測やWindows IME有効化を行わない。

## 3. インターフェイスと結果

```powershell
# 合成回帰のみ。OS TSF/レジストリを読み書きしない。
python tools/test_meltia_t4b4_native_artifact_gate.py
python tools/test_meltia_t4b5_offline_review_gate.py

# 以下は、隔離VMで別途取得・保存しレビュー済みの場合だけ。
# 証拠ファイルの読み取りとステージPE再検査のみ、登録はしない。
python tools/meltia_t4b5_offline_review_gate.py --t4a <t4a.json> --tsf <tsf.json> --attributes <t4b3.json> --staging-root <T3a-stage>
```

結果の `checks` は `registry_and_profile`、`display_attributes_and_categories`、`staged_native_artifacts` の3ゲートを区別する。`blocked_by` には実VMの独立スナップショット、所有権receipt、cross-user/権限差、本家共存、関数実行・T5打鍵といった**未解決ゲートを必ず残す**。`reason_code` は固定コードで、ローカルパス・観測した利用者情報を含めない。

- **観測済み衝突:** `blocked-occupied`（exit 1）。既存Meltia GUID資源も新規導入としては衝突扱い。
- **欠損・不整合・観測不可:** `blocked-inconclusive`（exit 2）。属性の `not-found` も全スコープでの不在を証明しないため止める。
- **全て人工的に整った証拠:** やはり `blocked-inconclusive`（exit 2）。実行可能な登録コードは存在せず、レビュー承認へ自動遷移しない。

T4-A/B1/B3の各JSONは**本ツールが実VMから採集したものではない**。入力ファイルを改変できる攻撃者に対する真正性・鮮度保証もない。別VM・別セッションの記録を突き合わせても検出できない場合がある。ログの合否は「同じOSから取得した」「同じタイミング」「所有権を確立した」という証明には使えない。

## 4. テストと残る工程

`test_meltia_t4b5_offline_review_gate.py` は以下を確認する。

- 正常形式の40件＋可視プロファイル＋属性／カテゴリ＋人工x64/x86 PEでも登録禁止
- HKLM候補キー占有、外部/自己CLSIDのprofile・属性・カテゴリ占有
- 片アーキテクチャ欠損・逆アーキテクチャのDLL、設計マニフェストの登録ON改変
- GUID相違、40件欠落、JSON重複キー、BOM、巨大ファイル、シンボリックリンク
- 標準出力へのホスト固有の絶対パス漏出なし、CLIが常に非0終了

**次の条件が揃うまでT4-B登録／解除に進まない。** 独立のWindows VMスナップショット、T4-A/B1/B3の実環境観測手順のレビュー、登録資源の実OS所有権とpre/post差分、DLLの正規build由来と登録拒否挙動、逆順復旧試験、Meltypeとの共存を別PRで審査する。CIでパスしただけでは実機使用可能にならない。親Issue #16はOpen継続。

## 5. 実施記録

- 2026-10-21：mainにPR #92（T4-B4）が取り込まれ、登録なしの構造監査まで進行。T4-B5では**複数証拠の相互整合確認・停止理由の一元化だけ**を追加する。実Windowsレジストリ、TSF、メモ帳のキー入力やロールバックを実行した記録ではない。
- PRのWindows Preview CIで合成テストと既存回帰が終わった後、結果を別途確認する。**レビュー完了前にmainへMergeしない。**
