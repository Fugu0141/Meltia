# T4-C1 — 隔離Windows VMのTSF証拠採集・再現性チェック（2026-10-21）

> **要件定義・基本設計・無登録実装。** T4-A、T4-B1、T4-B6を**同じオペレーターセッションで順番に実行**し、T4-B5が読める形で保存する手動VM試験支援ツール。WindowsのTSF実登録・解除、IME切替、インストール、実打鍵は**一切実施しない**。CIでは合成結果だけで流れを検証する。

親：[Issue #16](https://github.com/Fugu0141/Meltia/issues/16)。依存：[T4-A](MELTIA_T4A_READONLY_REGISTRATION_PREFLIGHT_20261021.md)・[T4-B1](MELTIA_T4B1_TSF_PROFILE_AUDIT_AND_ROLLBACK_GATES_20261021.md)・[T4-B5](MELTIA_T4B5_OFFLINE_EVIDENCE_RECONCILIATION_20261021.md)・[T4-B6](MELTIA_T4B6_VM_TSF_OBSERVER_20261021.md)。

## 1. 現状と目的

T4-B6までで**単独の**レジストリ候補キー検査、可視TSFプロファイル列挙、表示属性・カテゴリ問い合わせは準備された。ただし実VMでの採集結果がなく、単独の証拠ファイルには同一セッション・同一GUIDマニフェストに関する採集経路の記録がない。本工程はそのギャップを埋める。

**対象外**：実VMを用意すること、VM隔離・スナップショットの真正性を証明すること、第三者COM providerの無副作用の証明、他ユーザー・他権限への網羅、TIP DLLの登録拒否実行結果、登録前後の実所有権receipt、実登録・rollback・本家共存・実打鍵。これらは今後の独立したVM試験・別PRに残す。

## 2. 受け入れ条件・基本設計

| 手順 | CLIと判定 | 次の実OS呼出し |
| --- | --- | --- |
| 事前条件 | Windows・CIではない・復元可能なVMスナップショット確認・COM provider活性化リスクをオペレーターが承認・T2設計専用で登録OFF | すべて揃わない場合は**何も調査せず終了コード2** |
| T4-A | 既存PythonでHKLM/HKCU・32/64bitの40候補キーを読み取り | **完全にprecheck-clear／exit0のみ**B1へ。他は停止 |
| T4-B1 | 既存MSVC実行ファイルで全言語の現ユーザー可視TSF profileを列挙 | **完全にenumerated-clear／exit0のみ**B6へ。他は停止 |
| T4-B6 | T2の生バイト一致・属性GUID役割一致を前提に3属性＋5カテゴリを実照会 | 属性がresolved・カテゴリ占有ならexit1（blocked-occupied）、その他はexit2（blocked-inconclusive）。常に登録禁止 |
| 証拠 | 結果JSONをサイズ・スキーマ・終了コードで再検査し、私的な新規フォルダーにのみ保存 | 元データが不正/欠損/異常終了の場合は原因コードのみ記録し、後続の危険な呼出しをしない |

- 出力は `t4a.json`、`t4b1.json`、`t4b3.json`（実行した段階のみ）と `review-summary.json`。収集結果にはT2マニフェストと観測ツールバイナリのSHA-256を添付する。終了時に元の実行ファイルを再ハッシュして変更検知する。
- `review-summary.json`の `independent_vm_provenance_verified=false` と `actual_vm_snapshot_verified=false` は**必ず維持**。スナップショットを口頭・CLIで確認したことは独立の証明ではない。CI合格・ハッシュ一致・占有不検出も登録許可ではない。
- 標準出力は限定的な結果コード・状態のみ。プログラムのstderrや外部provider名を標準出力へ書かない。元JSONはGUID等を含み得るため、**公開リポジトリへコミットしない**。保存先は既存ディレクトリやソースツリーを許可せず、事前に空の新規privateディレクトリを要求。
- `tools/build-meltia-t4c1-vm-observers.ps1`はx64/x86のB1・B6観測実行ファイルをMSVCでビルドし、**合成セルフテストのみ**動かす。C++ヘッダー生成には既存T2バリデータを使い、本家登録スクリプトやTIP DLLには触れない。
- `tools/meltia_t4c1_vm_evidence_capture.py`は外部コマンドを`shell=False`で実行し、90秒以内の限定的なJSONを検証。stdoutはWindows対応の専用読み取りスレッドで**読み取り中から128KiB上限を適用**し、超過時は子プロセスを終了する。stderrは破棄し、外部providerの診断文字列を表示しない。実VM経路は明示の3フラグがない限り不可。注入runnerの合成テストではOS APIや観測プロセスを起動しない（別途、無害なPython子プロセスで出力量上限のみテスト）。
- **レビュー #95 による強化：** T4-A/B1/B6のstdoutは正しいJSON構文であってもトップレベルがオブジェクト以外（`[]`、`null`、文字列）なら拒否する。後続COM照会はせず、限定された`blocked-inconclusive`・終了コード2を返す。出力先は、未作成ディレクトリの**親symlink / Windows junctionも解決してから**実体パスでcheckoutへの包含を確認する。ビルド専用PowerShellもPythonの同じ検証関数を再利用して、別名パスからのソースツリー内への保存を拒否する。

## 3. 隔離VMオペレーター向けの実行例

**復元可能なWindows 10/11 VMのスナップショット取得後にのみ実行。** 公開CIランナー、個人の普段使いのWindows、他人の端末では実行しない。外部の表示属性providerがCOM経由で活性化する場合があることを了承したうえで実施する。**こちらからは現在VMの操作・実測は行っていない。**

Windows SDK/MSVC x64/x86とPythonが入ったVMで、MeltiaリポジトリをカレントディレクトリにしたPowerShellから：

~~~powershell
# 1. ビルド・配置はリポジトリ外の新規ディレクトリで行う（OS観測なし）
$stage = Join-Path $env:TEMP ('meltia-t4c1-build-' + [guid]::NewGuid().ToString('N'))
./tools/build-meltia-t4c1-vm-observers.ps1 -Output $stage

# 2. VMスナップショットを用意・確認し、復元方法を検証したあとだけ手動で実行
$out = Join-Path $env:TEMP ('meltia-t4c1-evidence-' + [guid]::NewGuid().ToString('N'))
python tools/meltia_t4c1_vm_evidence_capture.py --vm-readonly --snapshot-confirmed --provider-activation-acknowledged --profile-exe (Join-Path $stage 'x64/MeltiaT4bProfileInventory.exe') --attribute-exe (Join-Path $stage 'x64/MeltiaT4b6VmTsfObserver.exe') --output $out
# 最終終了コードは必ず1（確認済み占有）または2（不確定・通常の陰性）
# $out\review-summary.json と段階別のJSONを非公開レビューへ
~~~

x86版の観測が必要な場合は**別の新規出力先**を指定し、`x86/`の2実行ファイルを用いて同じ操作を独立して行う。x64で見えなかったことはx86での不在を示さない。

### 追加のオフライン統合審査

別工程で作成した **T3a登録拒否** のステージ（`bin/x64/MeltiaTip.dll`と`bin/x86/MeltiaTip.dll`を含む、同一の隔離ビルド）の在処・由来を確認し、3つのJSONが得られた場合のみ、既存B5を**読むだけの**レビューに使う：

~~~powershell
python tools/meltia_t4b5_offline_review_gate.py --t4a (Join-Path $out 't4a.json') --tsf (Join-Path $out 't4b1.json') --attributes (Join-Path $out 't4b3.json') --staging-root '<externally staged T3a root>'
~~~

B5も**必ずexit1/2の停止**。B1/B3が欠けている場合は不足証拠と明示したまま停止する。**TSF登録コマンドの実行は禁止**。

## 4. 本PRの回帰テスト

~~~powershell
# OS/COM/registry一切呼ばず、合成JSONだけ
python tools/test_meltia_t4c1_vm_evidence_capture.py

# Windows CIではMSVCのx64/x86ビルドと --selftest のみ
$stage = Join-Path $env:TEMP ('meltia-t4c1-ci-' + [guid]::NewGuid().ToString('N'))
try { ./tools/build-meltia-t4c1-vm-observers.ps1 -Output $stage }
finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
~~~

負例：登録ON T2、Windows以外/CI、ACK不足、T4-A占有・未知、T4-B1占有・途中失敗、B3占有・不正終了コード、異常JSON・重複キー・トップレベル配列/`null`/文字列、CLI終了コード2と匿名化した停止JSON、サイズ上限超過（子プロセス実行中に検知）、外部runner異常、入力バイナリ不存在・既存出力フォルダー・checkoutに向く親symlink/Windows junctionを拒否。

**レビュー対応と検証範囲（2026-10-21）:** PR #95のP2-1（非辞書型JSON）とP2-2（パス別名によるcheckoutへの書込み）を修正し、P3（観測プロセスのメモリ上の無制限バッファ）についてもstreamingの上限を追加した。上記の負例回帰を追加。**実VM観測・OSへのIME登録は実行していない**。CIの成否は最新の実行結果を別途確認すること。親ディレクトリを他プロセスが同時に差し替える競合、外部COM providerの副作用、VM真正性までは保証せず、隔離VM・非公開保管・手動確認が引き続き必須。

## 5. 将来へ引き継ぐ作業

1. オペレーターが用意した隔離VMで実観測し、**非公開の証拠パック**と環境・権限・スナップショットの記録を保管。結果を専門レビューする（本PRでは未実施）。
2. 影響を受ける他ユーザー・権限・x86 TSF・第三者providerの可視性を実験し、T4-B5の未解決ブロッカーを整理する。
3. 正規x64/x86ビルド・登録拒否・所有権receipt・逆順rollback試験のための**独立の登録検証PR**を起こす。保護のない自動登録スクリプトや本家の`Register-Tip.ps1`は流用しない。
4. Windows IME登録の実証後にT5日英混在入力E2E、T6配布の順で進む。

親Issue #16をこのPRのみでCloseしない。Markdownに残すのは手順と**実施済みの検証結果だけ**とし、実VM未実行を明確に区別する。
