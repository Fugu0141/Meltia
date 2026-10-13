# Issue #32: `changesuru` → `changeする`（Windows Preview、2026-10-14）

## 1. 報告・再現手順

- 対象: [Issue #32](https://github.com/Fugu0141/Meltia/issues/32)、Windows 11 / Chrome。
- 操作: ローマ字で `changesuru` を順番に打鍵（空白・記号なし）。
- 報告された実際の結果: `ちゃんげする`。
- 期待される結果: `changeする`。入力した生のキー列 `changesuru` が消失・重複しないこと。

## 2. 原因の分析

`change` は Meltype の同梱 `english.txt` に含まれるが、`cha` + `n` + `ge` はローマ字としても読める。そのため、読みだけに依存すると「ちゃんげ」に吸収される。ネイティブの `CompositionDetector.Segment` には「ローマ字で読めない英単語 + する形」の先行分割があるが、ローマ字として読める `change` はこの条件に入らない。

Windows Preview の `MeltiaLexicalRecovery` は、(1) 正規の英語辞書にあり、(2) 英語の綴りに特徴的な文字（`c/l/q/v/x`）を含む強い英語根拠、(3) ユーザーが日本語として明示学習していないこと、(4) 元のかな単位の境界で安全に切れることを前提に、既存の英単語 + 助詞を保護していた。しかし助詞ではない `suru` は対象外だった。

## 3. 設計・修正範囲

- **Preview の語彙保護だけを変更**。既存の「強い英単語」判定・かな単位境界確認・保護区間拒否・生のキー列の完全保持は変更しない。
- 英語区間の直後が、完成した小文字の `suru` **ちょうど4文字**である場合に限って、既存の助詞条件と同じ安全審査で `英語 + する` に分割する。
- `su`、`sur`、`suru...` の曖昧な入力途中・派生形は自動確定しない。部分一致を追加せず既存の判定に委ねる。
- 明示的な日本語学習（`change`／入力全体）、protected span、手動判定、ローマ字で読める未知語／根拠の弱い英単語は保護の対象外。
- **本家 Meltype の Core、Meltia Core の研究アルゴリズム、辞書、IME のキー処理・確定処理は変更しない。** 実験的境界 ON/OFF の双方で、Preview の既存の常時有効な語彙保護を通す。

## 4. テスト計画と合格条件

`src/Meltia.Windows/MeltiaPreviewSelfTest.cs` のフックなしセルフテストに再現・回帰ケースを追加:

| テスト | 受け入れ条件 |
| --- | --- |
| `changesuru` / 実験境界 OFF と ON | 一文字ごとに Raw が完全保存され、最終的に `change`（英）+ `suru`（日・`する`）、最終表示 `changeする` |
| `Changesuru` | 先頭大文字 `Change` を維持して `Changeする` |
| `chakusuru`, `nihongosuru`, `tokyosuru` | 新たな語彙保護が発火しない |
| `changesurux`, `changezuru` | 完成した `suru` 以外に拡張しない |
| 日本語として `change` を明示学習した状態 | ユーザーの日本語学習を上書きしない |
| protected span | 既存保護区間を上書きしない |
| 既存 Preview テスト | `commitha`, `meltiawotamesu`, `API`, `Ado`, 日本語のみなどを保持 |

CI は `.github/workflows/meltia-windows-preview-ci.yml` で .NET 10 の Meltype Core 回帰・Windows Preview publish/セルフテスト・研究 Core 品質を確認する。

## 5. 検証状況・制約

- 静的確認: `change` は同梱英語辞書に掲載。分割位置 `change|suru` は原文6文字目で、元のかな単位境界に一致する想定。
- 作業環境に .NET SDK がないため、**ローカルでの実行テスト・再現テスト成功は未確認**。PR の Windows Actions で実コードの結果を確認する。
- Windows 11 / Chrome の実打鍵、途中表示のフリッカー、Enter/変換確定後、IMEフック・TSF の E2E は未検証。CI 合格をもって実機問題の完全解消とみなさない。
- この Issue の受け入れ範囲を超える活用形（`して`、`した`等）や曖昧語のモデル改修は次期要件定義で検討。

## 6. 確認手順（Windows 実機）

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-meltia-windows-preview.ps1
```

実験境界 OFF/ON で、Chrome とメモ帳に `changesuru`、`Changesuru`、`change`、`commitha`、`meltiawotamesu`、`nihongosuru` を順次入力し、未確定表示・確定後表示・原文保持をそれぞれ確認する。実キー結果はこの文書へ追記する。

コード・テストの作成には生成AIを使用。静的コード／辞書を確認したが、実行環境の限界と未実施項目を上記に区別している。
