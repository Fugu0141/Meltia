# T4-C2 — 無登録VM観測証拠のオフライン整合・衝突レビュー（2026-10-21）

> **要件定義・基本設計・実装契約。** T4-C1が生成した非公開観測パックを、T4-B5の既存検査器と突き合わせる。**Windowsレジストリ/COM/TSF呼び出し、登録/解除、TIPロード、外部プロセス起動、データ書込は一切しない。** 合成CIでの合格は実VM観測の実施を意味しない。

親：[Issue #16](https://github.com/Fugu0141/Meltia/issues/16)。
前工程：[T4-C1](MELTIA_T4C1_VM_EVIDENCE_CAPTURE_20261021.md)、
[T4-B5](MELTIA_T4B5_OFFLINE_EVIDENCE_RECONCILIATION_20261021.md)。

## 1. 現状、解く問題、対象外

T4-C1はT4-A・B1・B6を順番に実行して `t4a.json`・`t4b1.json`・`t4b3.json` と `review-summary.json` を保存する。ただし従来は、保存済み観測ファイルごとのハッシュをサマリーに残さず、**採集後にサマリーと各段階の結果が一致しているか**を専用ツールで再検証できなかった。また、T4-B5で正規のx64/x86ステージをオフライン構造監査できるが、C1の中断理由との整合は別途人手で確認する必要があった。

本工程は、C1の観測ファイルを保存した際にSHA-256を記録し、**別の読み取り専用CLI**でC1の順序・結果・ダイジェストとB5の既存検査を確認する。

対象外：隔離VMの作成とスナップショット認証、観測ファイルの署名・攻撃者による一括改変の検出、ツールの実行ファイルと報告ハッシュの独立照合、x64/x86双方や別ユーザーのカバレッジ保証、正常なNative DLL起源の証明、登録拒否stubの実行検証、レジストリ所有権receipt、逆順rollback、正式IME登録、実機E2E。

## 2. 受け入れ条件

1. T4-C1の `review-summary.json` に、**実際に保存した各段階のJSONバイト列**のSHA-256（`evidence_sha256`）を記録する。ハッシュは改変検出用であり、独立した証拠真正性証明とは見なさない。
2. `--bundle` は既存のプライベートフォルダーのみ。JSONは最大128KiB、シンボリックリンク・重複キー・配列型などを拒否する。未知のファイルは読まない。
3. T2マニフェストは引き続き `design-only`・登録OFF。観測のmanifestハッシュとの一致を確認。観測実行ファイルの二つのハッシュは構文のみ確認し、**実ファイル起源を検証したと主張しない**。
4. T4-C1の `stage_checks` を信用せず、保管された証拠と対応する終了コードを元にC1の **pure `capture()` を再実行**。T4-Aが占有/不明ならB1を実行した証拠を整合と扱わず、B1が占有/不明ならB3を実行した証拠を整合と扱わない。
5. サマリー・証拠・ハッシュの不正/欠損は `inconsistent`。ただし**正常形式の独立した観測で検出した衝突を、別ファイルが破損したという理由で隠さない**（衝突は `blocked-occupied`、不整合時の理由は `potential-identity-conflict-in-unverified-bundle`）。
6. B5のNative x64/x86 PEチェックを既存APIで再利用する。結果が不明でも非許可のまま。出力は匿名化した判定カテゴリのみで、実際のGUID、第三者情報、パスを含めない。
7. **正常に見える合成データでもexit 2**、衝突はexit 1、exit 0は禁止。登録許可・レジストリ更新・DLL実行・自動rollbackフラグはすべて `false`。Preview既定設定、上流Meltype資産、H11アルゴリズムは変更しない。

## 3. 処理順

1. T2マニフェストを読み、登録OFFを検証（失敗時は停止）。
2. 非公開C1バンドルの四つの限定ファイルをそれぞれ**独立して**読み取る。読めないファイルは未確認扱いにし、他のファイルの占有証拠を捨てない。
3. 既存B5の `assess()` を呼び、各ファイルからの占有を先に集約。保存済みNativeバイナリのPEヘッダーを読取検査。
4. C1のサマリー契約・SHA-256・T2マニフェストとの一致と、ステージ順・結果の再現性を照合。**T4-Aの終了コードはC1の正規化済み`stage_checks`ではなく、保管されたT4-A観測JSONを`validate_t4a()`で検証した元の`status`から復元する。** `occupied>0`かつ`unknown>0`なら、T4-Aは`inconclusive`/exit2、C1は衝突を優先し`occupied`へ正規化する。
5. 非認可レポートを標準出力のみへ返す。**どの分岐でもOSへの書込・登録・サブプロセス起動は行わない**。

### オペレーターによる非公開レビュー

```powershell
# T4-C1で実際に収集した PRIVATE フォルダーと、別途T3aの無登録ビルドがある場合
python tools/meltia_t4c2_offline_bundle_review.py --bundle '<private-T4-C1-bundle>' --staging-root '<T3a-isolated-stage>'
# 戻り値: 常に1(衝突)または2(不確定/正常に見える場合を含む)。
# 0は一切出さない。登録・解除は実施しない。
```

`bundle_consistency=internally-consistent-unverified` は、そのバンドル内で値が整合する**という限定的意味のみ**。`bundle_origin_authenticated=false` と `independent_vm_provenance_verified=false` は常に維持する。C1の古いバンドル（`evidence_sha256`がない）は `inconsistent` と判定し、登録可能にはしない。再取得が必要なら必ず隔離VMスナップショットから手動で行う。

## 4. 合成テストと未解決ゲート

```powershell
python tools/test_meltia_t4c1_vm_evidence_capture.py
python tools/test_meltia_t4b5_offline_review_gate.py
python tools/test_meltia_t4c2_offline_bundle_review.py
```

C2負例：T4-A/B1/B3での衝突、後続未実行、中間JSON削除・形式異常・symlink、ハッシュ改変・欠落、サマリー欠落・重複キー、偽の登録許可・manifest差し替え、ステージ順改ざん、x86構造監査失敗。既存T4-C1への変更には、保存済みJSONとSHA-256が一致する模擬CLIテストを追加する。

**実施範囲:** 新しいオフラインコードと合成テストの検証のみ。Windowsの実VM観測、TSF登録、IME有効化・実打鍵は行っていない。別ユーザーと権限、VM真正性、第三者COM provider活性化リスクを確認できないため、Issue #16とT4登録許可ゲートは引き続きOpen。

## 5. PR #96 レビューP2修正（2026-10-21）

- **原因：** C1保存サマリーの`stage_checks.t4a=occupied`は「元のT4-Aがexit1だった」という意味ではない。T4-Aの40候補のうち、例えば1件が`occupied`、1件が`unknown`、38件が`absent`なら、T4-A元の`status=inconclusive`/exit2をC1が検証した後、観測済みの衝突を優先して`stage_checks.t4a=occupied`・`blocked-occupied`に正規化する。
- **修正：** C2の`replay_runner()`は元の観測レコードから`validate_t4a()`で真の状態を復元してexit2でC1再生する。正規化済みステージ判定を終了コードへ逆変換しない。
- **追加回帰：** 純粋な`occupied`/exit1に加え、`occupied+unknown`/exit2、`unknown`のみ/exit2をC1の`capture()`からJSON保存・C2の`assess_bundle()`まで合成実行。混在が`internally-consistent-unverified`かつ`blocked-occupied`になること、不明のみは`blocked-inconclusive`のままになることを確認する。
- **境界：** いずれも登録許可にはならない。OS実観測や証拠の第三者真正性は引き続き未検証。

## 6. 次工程（レビュー後に別PR）

実VMでのプライベート観測の適切な保管・同一スナップショットの記録、観測実行ファイルの独立ハッシュ照合、x86/ユーザー/権限による可視性差、T3a DLLの登録拒否実測、所有権receiptとrollback計画を分離してレビューする。**これらの証拠を欠いた状態でOSへのTSF登録を自動化しない。**
