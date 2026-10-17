# T3a — Meltia Native TIP隔離ビルドの要件・実装・未完了ゲート（2026-10-17）

> **試験用DLLをx64/x86でビルドするだけ。TSF登録・レジストリ操作・Meltia実用IMEの起動・配布はしない。** 元のMeltypeのNative DLL、登録スクリプト、アイコン、ビルド設定は変更しない。

基準main：`703e3b5600b59d0b3a27027f0f73f907140dd37f`。[T2採番マニフェストと所有権](MELTIA_T2_TSF_IDENTITY_AND_REGISTRATION_BOUNDARY_20261017.md)と[T1-B IPC](MELTIA_T1B_TIP_IPC_PROTOCOL_CONTRACT_20261017.md)に従う。上位Issue[#16](https://github.com/Fugu0141/Meltia/issues/16)。

## 実装境界と要件

1. `config/tsf/meltia-t2-identity.json`を唯一のGUID情報源とし、**リポジトリ外の新規一時ディレクトリ**へNativeソースをコピー・Meltia用に変換する。`native/tip`は一切編集しない。
2. `Globals.h`内のCLSID、profile、3種の表示属性GUIDと表示名をマニフェスト値へ差し替える。`Pipe.cpp`は`Meltia.Tip.v1.<SID>`へ分離し、`TextService.cpp`の自プロセス識別をMeltia.exeへ変更、再注入マーカーを独立させる。
3. `Dll.cpp`のログ先・有効化環境変数をMeltiaへ分離。`DllRegisterServer`／`DllUnregisterServer`は**どちらも`SELFREG_E_CLASS`を返して登録や解除を実行しない**。試験用に残るCOMクラスファクトリーとコンパイル済み機能の**安全性を保証するわけではない**。
4. `MeltypeTip.rc`から本家アイコン参照を削除し、製品表示文字列をMeltiaに変更。`MeltiaTip.def`へ改名、出力を`MeltiaTip.dll`にする。**旧インストーラー・本家アイコン・登録スクリプトはコピーしない。**
5. 前提のソース文字列が変化した場合は**例外で停止**し、黙って本家識別子でビルドすることを許さない。既存の出力先やリポジトリ配下への生成も拒否する。
6. CIでは`tools/test_meltia_t3a_stage.py`の静的/一時生成テストと、Windows上の`tools/build-meltia-tip-no-registration.ps1`によるx64/x86コンパイルを実行し、公開アーティファクトとしてアップロード・配布しない。

## ビルド方法（Windows + VC++ Build Tools）

```powershell
python tools/test_meltia_t3a_stage.py
powershell -ExecutionPolicy Bypass -File tools/build-meltia-tip-no-registration.ps1 -Architecture Both
```

一時ステージは実ユーザーデータ/レジストリを読み書きしない。プロセスは`rc`/`cl`/`link`の実ビルドに限り、`regsvr32`などでの登録や`Register-Tip.ps1`の呼び出しを**禁止**。生成DLLは**未登録・実行非推奨・非配布**。

## T3aはT3全体の完了ではない

- `TipServer.cs`は依然`Meltype.Tip.<SID>`・本家GUID・本家モードのまま。**Meltia用C#サーバーはないため実通信は動作しない。**
- T2で要求した`version=1`のhello/unknown-versionのfail-closedハンドシェイク、C#⇔Nativeパケット互換、実際のTIPキー入力は未実装。**これはT3b**で専用C#サーバーとnativeを結合してから検証。
- T4以降の独立Windows VMでCOM/TSF登録の衝突検査・登録/解除の対称性・実入力E2Eを実施するまで、登録可能なDLLやインストーラーを作らず本家の資産にも触れない。
- 本家ソースをステージ生成で機械書換する構成は長期的な設計ではない。T3b/R2以降は所有境界を分割し、コード生成による単一定数ソースへ置き換える。


## CI検証結果（2026-10-17）

- PR：[#80](https://github.com/Fugu0141/Meltia/pull/80)。コード検証HEAD `d3c1cdaa805128197a44c690c07a59d7bfeb53ab`。
- [Windows Preview CI](https://github.com/Fugu0141/Meltia/actions/runs/39036244969)：T2既存契約 **9件PASS**、T3aソース生成・禁止操作の検証 **7件PASS**、継承Core **363/363 PASS**、研究品質 **1,067件+benchmark**のジョブ成功。
- `rc`+`cl`+`link`の実行で**x64とx86両方のMeltiaTip.dllビルド成功**。CIログの一時生成DLLのSHA-256：
  - x64：`F380A538F15C684A6DACC840C5D2BC653EA753071C9701F1AB1C4AAD9DFFD56F`
  - x86：`B011D5249898C1B4B640C288FED2A953147961D16949570C1377E07F51D92638`
- `pr-checks`、`build`、`Meltia Windows Preview CI`の**3ワークフローが成功**。
- 初回YAMLのステップ名に含むコロン、続くPowerShell変数のコロン、MSVCの`/Fo`/`/Fd`末尾バックスラッシュ引用符をそれぞれ修正。**成功した上記HEADが実行基準**。DLLをアップロード・登録・配布したわけではない。
