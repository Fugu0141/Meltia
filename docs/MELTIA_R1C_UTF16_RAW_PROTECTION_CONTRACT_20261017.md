# R1c — 原文UTF-16・保護区間・編集操作の回帰契約（2026-10-17）

> **既存CompositionTextを変更しないcharacterizationフェーズ。** OS/TSF/辞書/境界アルゴリズムの改修を混在させない。

基準main：`703e3b5600b59d0b3a27027f0f73f907140dd37f`。関連：[R1a](MELTIA_R1_COMPOSITION_BEHAVIOR_CONTRACT_20261017.md)、[R1b](MELTIA_R1B_INPUT_CAPTURE_ORDER_CONTRACT_20261017.md)、[#58](https://github.com/Fugu0141/Meltia/issues/58)、[#72](https://github.com/Fugu0141/Meltia/issues/72)。

## 要件定義

- `CompositionText.Append(char)`で1打鍵ごとの`Raw`が元のUTF-16コード単位を失わないこと。変換単位`CompositionUnit.Raw`と`Pending`の連結は`Raw`と一致する。
- `k` / `ky`等の未完成ローマ字とバックスペース、`Clear`による初期状態への復帰を確認。
- `AppendKana(raw,kana)`のUTF-16・Unicode文字コード保持を確認。サロゲートペアや結合文字の**コード単位**が失われないことを確認し、Unicode grapheme cluster単位の削除・レンダリングを保証するわけではない。
- URLとメールの保護区間は原文を参照するUTF-16開始・終端位置で定義され、既存`Segments`の保護属性付き区間で完全な原文を保持する。対象はテスト例のみでURL網羅や他の保護種別は未完。
- `Segments(final:false/true)`は同じ入力に対して呼んでも`Raw`を書き換えない。結果のキャッシュの安定性を確認するが、異なる`final`で区間が一致することまでは要求しない。

## 実装とテスト

`src/Meltype.Core.Tests/MeltiaR1cTextPreservationTests.cs` に8件追加：

1. 複数のローマ字/英字/略語を1コード単位ずつ追加したときのRaw保持
2. `ky`途中ローマ字のBackSpace、空状態への復帰
3. `AppendKana`のUnicode・結合文字・サロゲートペアと`Raw.Length`
4. `Append`のLatinアクセント/非BMP/日本語混在Raw保持
5. URL/メール全体保護とSegments原文の同一性
6. 保護URL末尾の1原文文字のBackSpaceと再入力
7. `Segments`複数回・`final`差によるRaw不変
8. `Clear`後の表示モード/ケース/保護区間のリセット

```bash
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release -- MeltiaR1c
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release
```

## 明示的に残す残課題

- **Unicodeの一文字=1キーの前提は誤り。** UTF-16保全と絵文字/結合文字をユーザー可視の書記素として編集できることは別の問題。現状を勝手に改善しない。
- 実際のIMEホストとMeltia Coreの境界射影/UTF-16インデックスの一致性は後続R2・E2Eで検証する。テストが指摘した退行はPR内で修正し、発見された製品バグは個別Issueに分離。
- 本PRは`CompositionText`の状態分割自体を達成しない。#58/#72はOpenを維持する。


## 検証結果（2026-10-17）

- PR：[#79](https://github.com/Fugu0141/Meltia/pull/79)、初回検証HEAD `6317fe9ac028aaec005b33d8705119a9357892dc`。
- [Windows Preview CI](https://github.com/Fugu0141/Meltia/actions/runs/39035865298)にて`MeltiaR1cTextPreservationTests` **新規8/8 PASS**、継承Core全体 **371/371 PASS**。
- `meltia-strict-quality`、`linux-artifact-ci`、`build`、`pr-checks`を含む**5つのPRワークフローが成功**。
- Native TIP・Windows実機キー入力のE2Eを実施したという意味ではない。今後のリファクタリング時は同じテストセットを再利用する。
