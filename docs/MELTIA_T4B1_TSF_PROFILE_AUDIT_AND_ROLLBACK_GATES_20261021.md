# T4-B1 — TSF公開APIによる既存プロファイル照合と登録前停止ゲート（2026-10-21）

> **要件定義・基本設計・読み取り専用の実装段階。登録・解除・IME有効化は未実施。** T4-B全体の完了ではない。T4-Aで40候補レジストリキーが不在でも、他のText Serviceが同じprofile GUIDを保持している可能性は残る。T4-B1では対象を明示し、**登録を許可しない**。

関連：親Issue [#16](https://github.com/Fugu0141/Meltia/issues/16)、[T2識別子](MELTIA_T2_TSF_IDENTITY_AND_REGISTRATION_BOUNDARY_20261017.md)、[T4-A](MELTIA_T4A_READONLY_REGISTRATION_PREFLIGHT_20261021.md)、[TSF/IME実機ゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 1. 要件と非対象

1. T2マニフェスト `config/tsf/meltia-t2-identity.json` のCOM CLSIDとlanguage profile GUIDだけを照合対象として受け取る。コマンドラインのGUIDは後段のPython検査でマニフェストと照合する。**GUIDの再採番・別ファイルへの固定複製は禁止**。
2. Windowsの `ITfInputProcessorProfileMgr::EnumProfiles(0, ...)` を使い、現在のユーザーから**可視な全言語のTSFプロファイル**に対して、同一CLSID・同一profile GUIDを**独立に**計数する。別のCLSID下に同じprofile GUIDがあっても検出する。既存プロファイルの詳細や利用者情報は出力しない。
3. TSF列挙が失敗・途中終了・上限超過したときは `inconclusive` にする。COM初期化・クラス生成の失敗も同じ扱い。**結果不明を未使用とみなさない**。
4. T4-Aの40件の各照会結果とTSF列挙結果を、Pythonの副作用なしの検査で突き合わせる。候補位置/GUID/件数/既定無効化フラグ/形式不整合は必ず停止。状態に関係なく `registration_authorized=false`。
5. x64/x86のWindowsビルドと合成プロファイル試験、Pythonの負例をCIで実施。ただし **CIはTSFの実環境列挙も、DLLの登録も、レジストリ照会も行わない**。
6. 本家Meltypeの登録・ファイル・設定・辞書、Preview既定Keyboard、Native TIPの登録拒否スタブは変更しない。

**この工程で調べないもの：** TSF表示属性GUIDの登録/所有関係、すべてのWindowsユーザーからの可視性、他ホストの登録、HKLM/HKCUの全登録状態、他コンポーネントのcategory associations、実際のMeltia DLLの登録可能性、登録中の排他性、登録/解除の巻き戻し、実キー入力。したがって `audit-clear-not-authorized` でも**Windows全体のGUID衝突なし・共存安全・導入可能**とは言えない。

## 2. 実装・インターフェイス

| ソース | 用途 | 実環境への副作用 |
| --- | --- | --- |
| `tools/meltia_t4b_profile_inventory.cpp` | TSF公開APIを通した現ユーザーに可視なプロファイルの照合。出力はGUID候補・集計JSONのみ | 登録/解除/活性化なし。読む対象は現ユーザーから見えるTSF列挙 |
| `tools/meltia_t4b_audit_gate.py` | 保存済みT4-A JSONとTSF JSONの同一性・整合性検証 | ファイル読み込みのみ。登録許可を表さない |
| `tools/test_meltia_t4b_audit_gate.py` | 欠損・偽集計・異なるGUID・一部列挙・他サービスのprofile GUID使用などの負例 | モック／合成値のみ |
| `tools/test-meltia-t4b-profile-audit.ps1` | MSVC x64/x86 コンパイルと `--selftest` | 一時ディレクトリ内のビルド生成と合成試験のみ。TSF列挙なし |

TSF helperの引数は `<CLSID> <profile GUID>` の2個だけ。正しい値は**T2マニフェストから取得**。結果JSONの `expected_clsid` と `expected_profile_guid` をPython側がT2と照合する。`complete=false` を成功としない。合計件数は最大100,000で強制打ち切り（不明扱い）。

TSF helperのexit codeは 0: `enumerated-clear`（**可視範囲で一致なし**）、1: `occupied`、2: `inconclusive`。Pythonゲートは 0: `audit-clear-not-authorized`（**登録禁止のまま**）、1: `occupied`、2: `inconclusive`。**exit 0をインストーラー・登録スクリプトの実行許可条件にしてはならない**。

## 3. テスト方法（無登録）

通常のWindows CIでは以下**だけ**を行う。

```powershell
python tools/test_meltia_t4b_audit_gate.py
./tools/test-meltia-t4b-profile-audit.ps1
```

2行目は `--selftest` のみ。Windows Registry/TSFの実状態を参照しない。Core/Preview/T2～T3gの既存回帰を保持する。

## 4. 隔離Windows VMでの手動取得（未実施）

VM内のスナップショットが取得済みであり、対象マシンが個別レビューで承認された場合のみ、読み取り専用で観測できる。**T4-B1のコマンドはいずれも登録をしない**。

1. T4-Aの既存手順をVMで実行し、標準出力JSONを `t4a.json` に保存する。Windows PowerShell 5.1・PowerShell 7のどちらでも以下のコマンドで取得できる（TSF/レジストリの変更は行わない）。結果が `occupied`/`inconclusive` なら原因を調査し登録工程へ進まない。

   ```powershell
   python tools/meltia_t4a_preflight.py > t4a.json
   ```
2. Visual Studio Developer Command Promptで以下のように読み取り専用ツールを **VMの作業用ディレクトリ**へコンパイルする（x64/x86ともビルド試験済みであることをCIで別途確認）。開発中のリポジトリやOSディレクトリへのコピー/インストールは不要。

   ```bat
   cl /nologo /std:c++17 /EHsc /W4 /WX /utf-8 /DUNICODE /D_UNICODE tools\meltia_t4b_profile_inventory.cpp /Fe:MeltiaT4bProfileInventory.exe /link ole32.lib uuid.lib
   ```

3. PowerShellでT2の値を読み、作成されたexeへ渡し、標準出力のみを `tsf.json` に保存する。TSF helperは全言語の現ユーザー可視プロファイルを列挙し、候補2つの一致数だけを公開する。**PowerShell 5.1の `>` はUTF-16LE（BOM付き）、PowerShell 7の `>` はUTF-8（通常BOMなし）** を生成する。監査ゲートは両方を明示的に受け付けるため、UTF-8への変換作業は不要である。

   ```powershell
   $id = (Get-Content .\config\tsf\meltia-t2-identity.json -Raw | ConvertFrom-Json).identity
   .\MeltiaT4bProfileInventory.exe ('{' + $id.clsid + '}') ('{' + $id.profileGuid + '}') > tsf.json
   python tools/meltia_t4b_audit_gate.py --t4a t4a.json --tsf tsf.json
   ```

   監査ゲートのJSON読み込みは**UTF-8（BOMなし/BOM付き）、UTF-16LE/BE（BOM付きに限る）** に対応する。BOMなしUTF-16やその他の不明なエンコーディング、破損したJSONは `inconclusive`（exit 2）で停止する。PS5.1形式とPS7形式の両入力、および不正なT4-Aカウンター型（`false`、浮動小数、文字列、欠損/余剰キー）の回帰テストを `tools/test_meltia_t4b_audit_gate.py` に追加した。**exit 0でもTSF登録の許可ではない。**

4. **両方の検査が成功しても登録しない。** 実環境の証拠はマシン名・SID・他製品名を含めない集計のみに限定してレビューする。観測したユーザー/権限/OSビルド/VMスナップショットを非公開の試験記録に残す。

## 5. 次に必要なT4-B2の設計・検証ゲート

- **属性GUIDの所有権**：input/converted/targetの各TSF表示属性GUIDが、他コンポーネントと競合・共有していないことの検証方法を確定する。T4-B1は確認できない。
- **HKLM/HKCU・32/64bit共存**：Meltia用COMクラスとprofileとカテゴリ所有ペアの登録範囲を明確化し、異なるユーザー/管理者と32bitアプリから独立性を確認する。
- **所有権台帳とトランザクション**：登録前snapshotと差分を用意し、Meltiaが新規作成した所有物だけを対称解除できることを示す。失敗時は直前のMeltia変更だけを逆順で復旧。既存キーの上書き・共有カテゴリ削除・本家キー削除は不可。
- **中断・手動復旧**：競合・不明・他製品との干渉・アンインストール不能では即停止。VMスナップショットの復元を最後の回復手段とする。実登録/解除は別PRのレビューと隔離Windows VMの実測が必要。
- **正式IME版とは区別**：登録が成功してもT5実キー入力・フォーカス・E2E・T6配布ゲートを満たすまでは正式IMEとみなさない。

## 6. 確認状況

- PR #89レビューP2/P3に対応：PS5.1 UTF-16LE/PS7 UTF-8証拠入力の明示的判別と、不正なJSON件数の厳格な整数型チェックを追加。実際のPowerShellからの取得ではなく**エンコード済みのテストファイル**で回帰を確認する（CI結果は個別確認）。
- 本PRは `--selftest` の合成プロファイル試験とPythonの純粋ロジック試験のみ。実WindowsユーザーのTSF一覧取得、登録/解除、IME選択、共存・ロールバック実験は**未実施**。
- Microsoft Docs：[ITfInputProcessorProfileMgr::EnumProfiles](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfinputprocessorprofilemgr-enumprofiles)（langid=0で全言語列挙）、[IEnumTfInputProcessorProfiles](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nn-msctf-ienumtfinputprocessorprofiles)。
