# Issue #100: 小文字 `node.js` の「ので.js」誤変換

- 対象: Windows Keyboard Preview の英字/かな境界（OS中立な `Meltype.Core` の継承Composition）
- 元報告: 2026-10-22 実機手入力、新境界判定ONで `node.js` → `ので.js`。`Node.js` は正常との報告
- 作業: 2026-10-25、専用ブランチ `fix/issue-100-node-js-boundary`
- 状態: 修正コードと回帰テストを追加。Windows実機E2E、.NET SDKによるローカル実行は**未実施**（実行可能なSDKが無い環境）。GitHub Actionsの実行結果はPRのChecksを参照

## 静的コード追跡と仮説

1. `CompositionText.Append` はRawを保持し、ローマ字を仮名のCompositionUnitへ正規化する。`node` は日本語の「ので」としても解釈可能。
2. ピリオド入力時は `Normalize(final: true)` 後、句読点への変換が発生し得る。後からアルファベットが続いても、直前の `.` のKanaを半角へ戻す条件は、略語・曖昧でない既知英語・固有名詞などに限る。
3. `CompositionDetector.FindSpans` の `DotSuffixEnd` は **ピリオドより前がすでに英語区間** であることに依存する。曖昧な英単語stemは拡張子を認識する前に日本語扱いのままになり得る。
4. `MeltiaPreviewBoundary.Refine` は `raw.All(char.IsAsciiLetter)` の条件により `.` を含む完成トークンを受け取らず、元のMeltype判定へfallbackする。よって **H11/Meltia Coreのスコア調整で直すべき不具合とは限らない**。
5. QAのFocusedには `node.js` があったが、表示/確定の期待文字列を要求するOracleではなかった。研究用GoldCorpusの `S09-node` もWindowsセッションの正解を保証しない。

この説明は**静的追跡での原因仮説**。手入力における各打鍵の状態遷移ログは未採取であり、唯一の実行時原因と断定しない。

## 狭い修正の設計

`CompositionDetector.Segment` に、次のすべてを満たす **完全一致のドット付き技術名** だけを英語の1区間として扱う経路を追加。

- 手動モード/かな入力でない。RawはASCII英字 + 単一 `.` + ASCII英字で、stemは3文字以上。
- stemが既知の英単語/固有名詞/学習済み英語である。
- 拡張子が限定されたソース系suffix集合（`js`, `jsx`, `ts`, `tsx`, `json`, `mjs`, `cjs`）に一致。
- ユーザー学習がstemやトークン全体を明示的に日本語とした場合には上書きしない。

理由: `node` を常に英語扱いにしたり、ピリオド以降の未知文字列をすべて保護したりすると、日本語の通常入力が英語に吸われるため。認識できる拡張子が既存の判定器の辞書に追加された場合でも、ここは **構造の強い証拠** に限定する。新アルゴリズムの選択/スコア/保護範囲と無関係な変更を混ぜない。

## 追加した自動テスト

- `src/Meltype.Core.Tests/MeltiaIssue100Tests.cs`: `node.js`、`Node.js`、`react.js`、`node.ts`、`node.json` を模擬Windows Keyboardで1文字ずつ入力し、未確定表示とEnter確定を一致検査。Raw UTF-16の保存、ライブ表示、final表示、セグメントの完全被覆、通常の日本語句点の非回帰も検査。
- `src/Meltype.Core.Tests/MeltiaQualityGate.cs`: `node.js\n` の最終ドキュメントおよびコミット文字列をオラクルに追加。文字欠落やハッシュ一致だけでなく**利用者に見える文字列**を検査。

## 実行/レビューゲート

```powershell
dotnet run --project src/Meltype.Core.Tests -c Release -- MeltiaIssue100
dotnet run --project src/Meltype.Core.Tests -c Release
dotnet run --project src/Meltype.Core.Tests -c Release -- --meltia-qa artifacts/issue100-quality.json 10000
```

CIは上記を可能な範囲で確認する。改変後のWindows Previewを同一環境・同一辞書・同一学習状態で OFF/ON に切り替えて手入力テストすること。予期される `node.js`, `Node.js`, `react.js`, `https://example.com`, `kyouhagoogledekensaku`, `commit`, `commitha`, `API`, `desu` の実機確認はCIだけで代用しない。

### 未完了の確認事項

- 実キー入力でのOFF/ON比較、誤判定に到達した最初のキーの特定
- 学習データによる既存の日本語優先設定との相互作用のWindows実機検証
- CIでの全テスト/品質ゲートおよびx64/x86 Windows Previewビルド結果
- ピリオド後の途中入力 `node.j` は完結したsuffixではないので、まだ全体を英語へ強制しない

本Issueの修正を先にレビューし、正式TSF登録や上流Meltypeへの仕様変更と混ぜずに扱う。
