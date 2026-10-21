# T4-C3 — 無登録TSF観測用EXEの別経路SHA-256照合（2026-10-21）

> **要件定義・基本設計・合成テスト仕様。** [Issue #16](https://github.com/Fugu0141/Meltia/issues/16) のT4-C2後続。**TSFの登録・解除、COM/レジストリ照会、DLLロード、Windows実機観測、OS打鍵、配布は実施しない。** 合成試験の成功は実VM由来の証拠を意味しない。

## 1. 背景と問題

[T4-C1](MELTIA_T4C1_VM_EVIDENCE_CAPTURE_20261021.md)は、TSF観測に利用した`MeltiaT4bProfileInventory.exe`と`MeltiaT4b6VmTsfObserver.exe`のSHA-256をC1の`review-summary.json`へ保存する。[T4-C2](MELTIA_T4C2_OFFLINE_EVIDENCE_BUNDLE_REVIEW_20261021.md)はJSON・ステージ順序・ハッシュ書式を照合するが、観測用EXEの**実バイト列**と比較していない。

今回の課題は、(1) C1が申告するハッシュ、(2) C1バンドルと別経路で記録・保全した期待ハッシュ、(3) レビュー担当が保持する観測用EXEの実バイト列のSHA-256を、**ファイルごとに**三方向比較すること。

**限界:** 別経路ハッシュはオペレーター入力なのでプログラムは真正性を認証できない。3値一致は「当時VMで本当にそのEXEが動いた」「独立ビルド起源」「スナップショットが真正」を証明しない。C1のハッシュを「別経路の期待値」としてコピーしてはいけない。

## 2. 受け入れ条件

1. C2の`assess_bundle()`を先に再利用し、証拠不整合やGUID衝突を検査。C2の`blocked-occupied`は、EXEが欠損・改変された場合も隠さない。
2. 読み取る実行ファイルは想定名の2つだけ。0バイト・8MiB超過・シンボリックリンク・親エイリアス・通常ファイル以外、読取中に長さ・mtime・inodeが変化した場合を拒否。実行・ロードしない。
3. 外部期待ハッシュは小文字64桁のSHA-256のみ。C1サマリーは既存の128KiB上限・重複キー拒否JSON読み取りを利用し、2つのEXEそれぞれで3値一致を要求する。
4. C2バンドルが`internally-consistent-unverified`でない限り、ハッシュだけ一致してもバイト照合完了扱いにしない。
5. JSON出力にはローカルパス・GUID・ハッシュ値・第三者情報を含めない。結果は`blocked-inconclusive`か`blocked-occupied`のみ。登録・書込・TIP DLL実行・自動rollback・真正性関連のフラグは**常にfalse**。
6. 終了コードはC2の確定衝突時`1`、その他`2`、**0は使わない**。Preview既定設定、Meltype本家の資産、境界判定/H11、C1/C2の保存スキーマは変更しない。

## 3. 基本設計

`tools/meltia_t4c3_observer_hash_review.py`を追加。C2のオフライン判定を優先した後、実ファイルを最大8MiBまでストリーム読込みでSHA-256計算する。連続した2回の読み込みで変化しないことまでは保証しないため、読込時の`fstat`前後照合とエイリアス拒否を用いる。C2レビューとC3レビューの間に外部プロセスが証拠バンドルを書き換える競合も防げない。**対抗的改変・署名認証の仕組みではない。**

`observer_binary_bytes_match_bundle_and_reference=true`は、C2内部整合と3値一致が確認された限定的な結果を表す。`observer_binary_origin_verified=false`、`reference_hashes_authenticated=false`、`independent_vm_provenance_verified=false`、`registration_authorized=false`は一致時も保持する。

### 非公開レビュー操作の例

```powershell
# 期待値はC1のreview-summary.jsonとは独立した記録から手動で入手する
python tools/meltia_t4c3_observer_hash_review.py `
  --bundle '<private-C1-bundle>' `
  --staging-root '<isolated-T3a-stage>' `
  --profile-exe '<preserved-MeltiaT4bProfileInventory.exe>' `
  --attribute-exe '<preserved-MeltiaT4b6VmTsfObserver.exe>' `
  --expected-profile-sha256 '<64-character-out-of-band-sha256>' `
  --expected-attribute-sha256 '<64-character-out-of-band-sha256>'
# 戻り値 1 / 2 のみ。登録・解除を行わない。
```

### 回帰テスト

```powershell
python tools/test_meltia_t4c2_offline_bundle_review.py
python tools/test_meltia_t4c3_observer_hash_review.py
```

一時ディレクトリの**合成ファイル**をEXE名として読み取る。真正EXEや実VMの試験ではない。正常一致、申告/別経路の不一致、差替え、欠損、シンボリックリンク、0バイト/過大ファイル、誤ったJSON構造、C2の衝突優先、秘匿出力、終了コード1/2を確認する。

## 4. 次工程と未解決

独立したVMスナップショット、OSビルド、操作者・権限と異なるユーザー視点（x86含む）の観測範囲、実行ファイルの独立ビルド起源・配布経路証明、T3a DLL登録拒否の実測、所有権receipt、復旧の逆順rollbackは未実施。実OSへのTSF登録は、これらを分離してレビューし、専用の隔離VM・スナップショット・明示承認が整うまで自動化しない。
