# T4-B4 — 無登録Native TIPのx64/x86 PE構造とSHA-256確認（2026-10-21）

> **要件定義・基本設計・read-only検査の実装。** T3aで生成した試験用`MeltiaTip.dll`を**ファイルとして読むだけ**。Native DLLのロード、COM/TSF API、`regsvr32`、レジストリ変更、実登録・解除、IME有効化、配布は行わない。**exit 0は「構造検査成功」の意味だけで登録許可ではない。**

関連：[親Issue #16](https://github.com/Fugu0141/Meltia/issues/16)、[T2マニフェスト](../config/tsf/meltia-t2-identity.json)、[T3a隔離ビルド](MELTIA_T3A_ISOLATED_NATIVE_TIP_BUILD_20261017.md)、[T4-B2所有権台帳](MELTIA_T4B2_OWNERSHIP_AND_ROLLBACK_CONTRACT_20261021.md)、[T4-B3表示属性・カテゴリ証拠](MELTIA_T4B3_ATTRIBUTE_CATEGORY_EVIDENCE_CONTRACT_20261021.md)（別PRでレビュー中）。

## 1. 調査・設計の根拠

- Meltype継承元`native/tip/Dll.cpp`にはWindows COM/TSFの登録・解除処理が含まれる。一方、T3aの`tools/prepare_meltia_tip_stage.py`は出力元から分離したステージへコードを生成し、`DllRegisterServer` / `DllUnregisterServer`を`SELFREG_E_CLASS`拒否スタブに差し替える。**この差し替えの検査はT3aソース試験が担当**し、PEヘッダー検査でDLLの無登録性を証明したとは主張しない。
- `tools/build-meltia-tip-no-registration.ps1`は新規一時ディレクトリの`bin/x64/MeltiaTip.dll`と`bin/x86/MeltiaTip.dll`を別々にコンパイルする。以前はビルドログにハッシュを表示していたが、アーキテクチャ取り違えを防ぐ**同一ステージ内の機械的な構造照合**がなかった。
- T4-B2のCOM x64/x86所有権台帳は抽象設計であり、実インストーラーへのファイル配置・バイナリの真正性・署名を確定していない。本工程はその前段として、ステージ上で**観測したバイト列のハッシュ**を記録する。取得時点以降の不変性・実ビルド由来であることまでは証明できない。

## 2. 受け入れ条件

1. 唯一の製品識別入力`config/tsf/meltia-t2-identity.json`について、`design-only`、`registrationEnabled=false`、4つのdeploymentフラグがすべて`false`、`assemblyBaseName=MeltiaTip`であることを検証する。
2. 既存のT3aステージに`STAGING_NOTICE.txt`があり、無登録・非導入の表示が保持されることを確認する。表示は**安全性の証拠ではない**。
3. 同じステージに`bin/x64/MeltiaTip.dll`と`bin/x86/MeltiaTip.dll`の**双方**が存在することを要求する。シンボリックリンク、巨大/空ファイル、壊れたPEヘッダーを拒否する。
4. DOS MZ・PE署名・COFF Machine（x64 `0x8664` / x86 `0x014c`）・Optional Header Magic（PE32+ / PE32）・固定ヘッダー長（96/112 bytes）・DataDirectory件数と宣言長・DLLフラグ・セクションテーブルの境界を最低限検査する。DLLをロードしたりexport関数を呼ばない。
5. SHA-256、バイト長、ステージからの相対パス、アーキテクチャをJSONとして**標準出力だけ**に返す。パスの絶対位置や環境識別子は出さない。判定成功時にも`registration_authorized=false`、`build_provenance_proven=false`、`registration_stubs_proven=false`。
6. 不正/片方欠損/不一致時は`inconclusive`（exit2）で止め、登録を開始しない。既存T3a単独アーキテクチャビルドに影響しないよう、ビルドスクリプトでのゲート実行は`-Architecture Both`の場合に限定する。
7. 合成PEバイト列による正例・異常例（2バイトOptional Header・DataDirectory個数超過を含む）をテストし、CIの成功を**実Windows VMのTSF動作やバイナリ真正性の証明と混同しない**。本家の登録処理、Keyboard Preview、境界判定・辞書・学習処理は変更しない。

## 3. コマンド（開発者／CI）

```powershell
python tools/test_meltia_t4b4_native_artifact_gate.py
powershell -ExecutionPolicy Bypass -File tools/build-meltia-tip-no-registration.ps1 -Architecture Both
# T3aビルド成功後、同じ一時ステージで読み取り専用監査を自動実行
```

個別の既存ステージを**読み取り専用**で検査する場合：

```powershell
python tools/meltia_t4b4_native_artifact_gate.py --staging-root <T3aの一時ステージ>
```

検査結果はJSONとしてstdoutに表示され、保存する場合のみ人手で別途保存する。`sha256`は観測したDLLバイト列の整合性比較用であり、配布用署名・信頼できるサプライチェーンの証明ではない。

## 4. 実施記録と未解決事項

- 2026-10-21：開発環境（Windows VMではない）で**人工PEヘッダーのみ**を使うPython試験20件を実行し、20件PASS。実`MeltiaTip.dll`、実x86/x64リンク済みバイナリ、Windows 10/11のTSF実機試験はこの記録では未確認。
- 既存WindowsビルドスクリプトのBothモードに検査を連結。Windows実ビルドの成功、生成DLLのexport拒否動作は**この文書からは確認不可**。CIの結果をPR上で別途確認する。
- T4-B3が扱う表示属性とTSFカテゴリ証拠の所有関係、HKLM/HKCU・x64/x86 registry view・ユーザー権限の衝突判定、登録セッションreceipt、独立スナップショット、実DLLからの関数呼び出し・本家との共存は未解決。
- 次の実行可能な登録・解除コード／インストール手順は別のレビューを必須とし、専用隔離VM・スナップショット・停止/手動復旧条件・本家データ保全が確認されるまでは導入しない。Issue #16はOpenのままとする。
