# T4-C4 — 隔離Windows VMの環境・スナップショット証拠台帳（2026-10-22）

> **要件定義・基本設計・合成テスト仕様。** 親 [Issue #16](https://github.com/Fugu0141/Meltia/issues/16)、前工程 [T4-C3](MELTIA_T4C3_OBSERVER_HASH_INDEPENDENT_REVIEW_20261021.md)。**実VMの構築・実測・スナップショット復旧確認、Windows COM/TSF/レジストリへのアクセス、登録・解除・実打鍵は本工程では未実施。** CIの合格や台帳の整合は証拠の真正性を証明しない。

## 1. 目的と境界

T4-C1〜C3では観測サマリー・段階JSONの整合と、観測用EXEの申告／別経路／実バイト列の三方向照合を行える。一方、**どのWindowsビルド・権限・アーキテクチャ・スナップショットで観測したと主張しているか**を限定的な形式で保存し、その主張が証拠バイト列と矛盾していないかは確認できない。

T4-C4は、利用者が**非公開で手動作成する自己申告の台帳**を後から読み取り、C3の判定と照合する。台帳の値はプログラムが採集したりOSから検証したりしたものではない。完璧に整合しても「独立したVM由来の証明」「実際にスナップショットへ復元できることの保証」「第三者providerの無副作用」にはならない。

## 2. 受け入れ条件

1. C3の `assess()`（C2/B5を含む）を先に使用。**既知のGUID衝突 `blocked-occupied` を、台帳欠損・破損・一致によって `blocked-inconclusive` に降格させない。**
2. `tools/meltia_t4c4_vm_snapshot_review.py` は**オフライン読取専用**。リポジトリ外の非公開台帳（UTF-8 JSON、128KiB以下、重複キー禁止、既定フィールド以外拒否）と、既存C1バンドルの最大4ファイルだけを確認する。シンボリックリンク／junction／FIFO／通常ファイル以外・過大ファイルを開く前に拒否。POSIXではノンブロッキング・nofollowを使用。バイト読取前後の識別子・サイズ・mtime変化を拒否するが、敵対的なアトミック改ざんを完全には防げない。
3. 台帳にはゲストOS/ビルド、x64ゲスト・x64/x86観測用バイナリ、観測権限（標準/昇格）、対象スコープ（現ユーザー限定）、ネットワーク隔離・復元可能なスナップショット・復旧演習・provider起動リスクの**確認済みとする自己申告**、匿名化されたスナップショット参照のSHA-256、観測UTC時刻、担当ロール（氏名やユーザー名ではない）を記録。
4. 台帳に記載したファイル名・SHA-256は、C1バンドルに現存する段階JSONと `review-summary.json` の**実バイト列**と完全一致すること。C1が途中で停止し存在しない段階は台帳にも書かない。余剰エントリ、欠落、サイズ超過、取り違え、スナップショット参照の前後不一致は拒否。
5. **登録・OS書込・外部実行・TIP DLLロード・自動ロールバックは常に禁止**。台帳がすべて整合しても `status=blocked-inconclusive`、exit 2。既知衝突は `status=blocked-occupied`、exit 1。exit 0なし。台帳に氏名・マシン名・パス・SID・GUID・ハッシュそのものを標準出力しない。
6. **データの真正性は未確認のまま**：`private_record_consistent_unverified` は限定的な整合しか示さない。 `operator_identity_authenticated`、`snapshot_restoration_verified`、`independent_vm_provenance_verified`、`cross_user_architecture_coverage_verified`、`registration_authorized` は常に `false`。
7. 合成テストを追加し、Windows Preview CIに接続。Meltype本家の資産、通常Keyboard Previewの既定モード、H11、正式登録手順は変更しない。レビュー完了までmainに統合しない。

## 3. 非公開台帳の形式

この例は**説明用の架空値**。台帳はソースツリーやGitHub Issue/PR/CI artifactへ貼り付けず、復旧手順とともに隔離・アクセス制限した場所へ保管する。観測自体は[ T4-C1 ](MELTIA_T4C1_VM_EVIDENCE_CAPTURE_20261021.md)の手動手順と独立して行い、C4 CLIはOS観測を起動しない。

```json
{
  "schema": "meltia-t4c4-private-record-v1",
  "origin": "operator-supplied-unverified",
  "environment": {
    "guest_os": "windows-11",
    "guest_build": "10.0.26100",
    "guest_arch": "x64",
    "observer_arch": "x64",
    "privilege": "standard",
    "visibility": "current-user-only",
    "network_isolated": true,
    "snapshot_restorable": true,
    "restore_drill_recorded": true,
    "provider_activation_risk_acknowledged": true
  },
  "snapshot": {
    "pre_capture_reference_sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
    "restore_drill_reference_sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
  },
  "capture": {
    "operator_role": "designated-vm-tester",
    "observation_utc": "2026-10-22T04:00:00Z"
  },
  "evidence_sha256": {
    "review-summary.json": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
    "t4a.json": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
  }
}
```

- `pre_capture_reference_sha256` と `restore_drill_reference_sha256` は**操作者が別途管理するスナップショット識別子のダイジェストの申告**。一致は文字列一致に過ぎず、VM製品・ID真正性や復元の成功を示さない。スナップショットそのものや個人情報は台帳へ格納しない。
- `evidence_sha256` は**台帳作成時に保管された各ファイルから独立に計算した値**を記入し、C1サマリーの値をコピーしない。例の `t4a.json` だけの構成はC1早期停止を想定するが、実際にはC3がC2の整合性を別途確認する。
- 32bit観測・別ユーザー・別権限は**各々の独立したC1バンドルと台帳**で扱う。同一台帳に複数セッションの主張を混ぜない。通常Windowsでの登録試験の承認には使えない。

### オフラインレビュー手順（非公開保存物にのみ実行）

```powershell
python tools/meltia_t4c4_vm_snapshot_review.py `
  --record '<private-operator-record.json>' `
  --bundle '<private-C1-bundle>' `
  --staging-root '<isolated-T3a-stage>' `
  --profile-exe '<preserved-MeltiaT4bProfileInventory.exe>' `
  --attribute-exe '<preserved-MeltiaT4b6VmTsfObserver.exe>' `
  --expected-profile-sha256 '<independently-kept-64-hex-digest>' `
  --expected-attribute-sha256 '<independently-kept-64-hex-digest>'
# 戻り値は1(衝突)または2(それ以外)だけ。成功=登録可能ではない。
```

PowerShellの行継続は行末バッククォートを使う（後ろに空白を置かない）。期待値はC1サマリーのコピーではなく別経路から取得する。

### PR #98 レビュー指摘と修正（2026-10-22）

- **P1 — 衝突優先:** C4が全段階を一括事前拒否していたため、無関係なt4b3 FIFOで正常なt4aのoccupiedを隠し得た。**C3→C2→B5の実衝突判定を先に行い**、その後C4独自のファイル種別・台帳整合を検査する。台帳失敗時にも `blocked-occupied` / exit 1を保持。
- **P2 — open直前のFIFO置換:** C2の段階読込とB5のJSON読込に、上限128KiB・lstat・非ブロッキング/no-follow open・読取前後のfstatとパス再照合を行う共通readerを追加。単なるC4の事前検査に依存しない。POSIXで通常ファイルをopen直前にFIFOへ差し替える実C2/C3経路の子プロセス（タイムアウト付き）試験を追加。Windowsの敵対的な同時置換を完全に排除する仕組みではない。
- **P2 — 台帳のcheckout混入:** 非公開台帳を読む前にソースツリー（正規化path）内を拒否し、疑似checkoutの負例を追加。機密のgit commitを防ぐ補助であり、OS権限機構ではない。
- **P2 — 余剰ファイル:** 許容する4つのファイル名以外の子要素があれば内容を読まずC4整合検査を拒否。ディレクトリ・symlink・特殊ファイルも拒否。ただしC2の独立衝突結果には影響させない。
- **統合回帰:** 実際のC2/C3/B5・合成occupied T4-Aに、破損したt4b3またはFIFOを注入して、常に衝突優先・非登録となることを検証する。

## 4. テストと未完了の依存

```powershell
python tools/test_meltia_t4c2_offline_bundle_review.py
python tools/test_meltia_t4c3_observer_hash_review.py
python tools/test_meltia_t4c4_vm_snapshot_review.py
```

C4の回帰対象：限定台帳の正常な形式（**非認可**）、C3衝突の優先、上流不整合、観測バイト変更・欠落・余分な段階、復旧識別子不一致・確認false、OS/権限/ユーザースコープ偽装、重複キー、過大入力、symlink・FIFO、秘匿出力と非0終了。

**次工程:** 隔離VMを手動で用意し実際のスナップショット作成・復元演習の記録、x64/x86・ユーザー/権限ごとの実観測を**非公開で**実施し、別レビューで独立性と副作用を評価する。それまではC4の記録を作っても「実VM観測済み」と扱わない。T3a DLLの登録拒否実測、所有権receipt、逆順rollbackの独立した設計/実機ゲートが残る。Issue #16はCloseしない。
