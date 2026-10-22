# T4-C5 — 隔離Windows VMの複数観測セッション申告マトリクス（2026-10-22）

> **要件定義・基本設計・合成テスト仕様。** 親 [Issue #16](https://github.com/Fugu0141/Meltia/issues/16)、前工程 [T4-C4](MELTIA_T4C4_VM_SNAPSHOT_EVIDENCE_REVIEW_20261022.md)。本工程は**実Windows VMへのアクセス、観測実行、スナップショット復元、TSF/COM/レジストリ操作、DLLロード、登録・解除、打鍵を行わない**。オフライン入力はすべて操作者の自己申告で、真正性の保証はない。

## 1. 背景・目的

C4は**1セッション単位**で限定的な台帳整合を検査する。実Windows 10/11のx64 VMで観測する場合、x64/x86観測用EXEと標準/昇格権限は**独立したセッション**として扱う必要がある。複数のC1バンドルを一つの台帳へ混合したり、同じファイルを4回数えて検証範囲達成と誤認することを防ぐ。

T4-C5では、非公開計画JSONに列挙したセッションについて**既存のC4→C3→C2→B5検査を再実行**し、各C4記録が宣言する次の4条件を集計する。

| 観測用EXE | 実行権限の申告 |
|---|---|
| x64 | standard |
| x64 | elevated |
| x86 | standard |
| x86 | elevated |

これらは「観測済み・OSの権限確認済み・他ユーザー確認済み」を意味しない。**違うユーザーや実際の32bit TSFクライアントの可視性、第三者provider副作用、Windows 10と11の両方、スナップショット復元の成功を証明しない**。OS版/ビルドを混在させて四条件達成として扱わず、必要ならOSごとに別の非公開計画を用意する。

## 2. 受け入れ条件

1. `tools/meltia_t4c5_multi_session_review.py` は読取専用。プロセス起動・OS観測・登録・解除・証拠の書込を一切行わず、`registration_authorized`等の許可フラグは常に`false`。CLIは既知衝突時exit 1、それ以外exit 2。**exit 0なし**。
2. リポジトリ外の**非公開**計画（JSON、上限128KiB、重複キー・余計な外側キー拒否、1〜8セッション）を、C4が提供する安全な通常ファイルreaderで読み込む。各セッションの絶対path・種類・期待SHA-256の文法を確認する。計画や証拠の内容をログ、Issue、PR、CI Artifactへ出さない。
3. **各セッションで既存C4を先に実行し、いずれかが`blocked-occupied`なら、ほかの記録の欠損・改ざん・重複に関係なく総合`blocked-occupied`を保持**。不正データから「不在」を推定しない。計画自体が読めない場合は`blocked-inconclusive`。 **bundleの位置が有効である限り**、台帳・observer・stagingのパス記述が不正でも安全な無効パスとしてC4へ渡し、C3/C2の独立した衝突発見を先に確認する。
4. C4レポートの`private_record_consistent_unverified`かつ`upstream_bundle_consistency=internally-consistent-unverified`かつ`upstream_observer_bytes_match`が成立し、非衝突のケースのみ**限定的な自己申告の一枠**として数える。C4レコードが呼び出し中に変わった場合は拒否。
5. 異なるセッションID・**異なるrecord/bundleパス**のみ集計対象。同じ台帳/証拠パックを複数の観測として数えない。4枠の申告に加え、ゲストOS/ビルドと自己申告スナップショット参照ダイジェストが共通で、全セッション内部整合した場合のみ`claimed_matrix_consistent_unverified=true`。1件の不正・不足・追加セッション・異なる申告があればfalse。
6. **結果は必ずブロック状態**。集計に氏名・SID・パス・VM名・ダイジェスト値・ビルド番号・第三者TSFデータを出さない。`cross_user_architecture_coverage_verified`、`snapshot_restoration_verified`、`operator_identity_authenticated`、`independent_vm_provenance_verified`は常にfalse。
7. C4以前のテストとPreview既定動作、Meltype本家のコード、H11の評価を変更しない。合成回帰とCI、Markdown、READMEを追加し、レビュー完了まではマージしない。

## 3. 基本設計と非公開計画

計画フォーマットは`meltia-t4c5-private-session-plan-v1`。各セッションごとに次の**すべて**のキーを置く。以下は説明用であり、値や実際のファイルはソースツリー/リポジトリに置かない。

```json
{
  "schema": "meltia-t4c5-private-session-plan-v1",
  "sessions": [
    {
      "id": "session-01",
      "record": "C:/PRIVATE/session-01/private-record.json",
      "bundle": "C:/PRIVATE/session-01/C1-bundle",
      "staging_root": "C:/PRIVATE/no-registration-T3a-stage",
      "profile_exe": "C:/PRIVATE/MeltiaT4bProfileInventory.exe",
      "attribute_exe": "C:/PRIVATE/MeltiaT4b6VmTsfObserver.exe",
      "expected_profile_sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "expected_attribute_sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    }
  ]
}
```

- この例は`session-01`だけなので**マトリクスは未充足**となる。4条件の実検証を宣言するには、別々の証拠バンドルとC4レコードの`session-01`～`session-04`を明示し、C4側の`environment.observer_arch/privilege`を適切に記録する。
- `expected_*_sha256`はC1の`review-summary.json`からコピーせず別系統で保存した期待値を記入する。SHA一致が実行履歴やVM出自を証明するわけではない。
- 各`record`はC4仕様を守る。`bundle`と`record`はリポジトリ外の私的な読み取り元とし、実際のスナップショット・復元演習については**人間が別の監査手順で確認**する。
- パスに空白がある場合もJSONの文字列を使用する。Windows環境の実パスはすべてその環境に合わせる。

### レビューコマンド

```powershell
python tools/meltia_t4c5_multi_session_review.py `
  --private-plan 'C:\PRIVATE\t4c5-plan.json'
# 1: どれか1セッションに既知の衝突を検出
# 2: それ以外（申告整合・不整合・証拠欠落を含め常に登録禁止）
```

本ツールは登録可否を`true`に変更する出口を持たない。出力の`claimed_cells_unverified`はアーキテクチャと権限の**宣言を重複なく整理しただけ**であり、OSやユーザー権限の真正性の検証ではない。

## 4. 手動の隔離VM実測・承認ゲート（ここでは未実施）

1. **オペレーター**が独立のWindows VMを用意し、ネットワーク隔離、確認可能な復旧用スナップショット、手動復元演習の記録を用意する。共有日常PCや既存Meltype環境で実行しない。第三者COM provider活性化のリスクを理解した上で手動承諾する。
2. [T4-C1のVM採集手順](MELTIA_T4C1_VM_EVIDENCE_CAPTURE_20261021.md)で、まず標準権限x64観測の非公開C1バンドルを1つ生成する。**衝突/未知があれば停止し、次のCOM照会や昇格試行で「成功」に持ち込まない**。有効な結果を得た場合のみ、運用者の判断と独立した復旧操作の後、ほかの対象セッションを検討する。
3. OS実測をしない別の担当者がT4-C2/C3の独立EXE照合を確認し、C4台帳の観測OS/権限/snapshotとSHAをC1とは独立に記録。C4の`true`は整合の**自己申告**の意味しか持たない。
4. 4条件が独立採集された場合に限りT4-C5で申告マトリクスをレビュー。**異なるユーザー、プロファイル、スナップショット復元の実証は別の専門レビュー**で評価し、検証済みという文章を証拠の外へ勝手に転載しない。
5. **登録・解除・Meltia TIPロードは依然として禁止**。新たに登録検証用の独立PRを起票し、再現可能な実VM検証と明示レビューを経てから判断する。**Issue #16を閉じない。**

## 5. テスト・完了定義

```powershell
python tools/test_meltia_t4c5_multi_session_review.py
python tools/test_meltia_t4c4_vm_snapshot_review.py
python tools/test_meltia_t4c3_observer_hash_review.py
python tools/test_meltia_t4c2_offline_bundle_review.py
```

同一レコード/バンドル再使用、4枠の欠落、VM/OS/ビルド/snapshot宣言不一致、改ざん・証拠欠損、予期せぬキー・無効ハッシュ、planの重複JSONキー/checkout内保存/過大ファイル、C4衝突優先、**実C4→C3→C2→B5の合成統合ケース**、CLIの1/2のみ・個人情報非出力を検査する。Windows/UbuntuのCIは**合成試験のみ**で、実VMに触れない。検証結果はPRのCI記録を参照し、文書中に未確認のPASSを記載しない。
