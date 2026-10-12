# Meltypeネイティブ入力処理の段階的最適化 (2026-10-12)

## 位置付け

この変更は **Meltia独立境界Coreの推定をIMEに採用する変更ではありません**。既存のMeltype入力正規化・境界判定に残っている問題を小さく切り出し、研究用Coreを将来安全に接続できる土台を整えます。本番TSF、自動確定、辞書、学習データ、インストーラー、公開バージョンは変更しません。

### 前の検証から引き継ぐ要件

- 打鍵の **Rawは完全保存**。IME用かな単位に分解した後も、英語の区間を元の英字で提示できること。
- 日本語を既定とし、英語・日本語の両方が成立する文字列は**キー列だけでは意図を特定できない**ことを明示する。たとえば `tokyo`・`ime`・日本語ローマ字に見える固有名詞は辞書一致だけで自動英語確定しない。
- `commit` / `commitshita` / `meltiawotamesu` / `japaneseno` などの多様な境界、およびURL・メール・UTF-16・ローマ字の複数表記を今後の正解付き評価に保持する。
- PR #6〜#10の独立研究ブランチと、本番ネイティブの改修ブランチを混ぜない。v1.0-devのTop1結果をネイティブIMEの性能と混同しない。

## 今回の変更

1. **大文字略語の初期母音を再分類** (`CompositionText.Append`)。`I`・`A`などは単独では一度かなに読まれていても、次の大文字が打たれたときに末尾の大文字ランを英字表示へ復元する。`IME`・`API`・`AI`の冒頭が「い」「あ」のまま残る経路を防ぐ。Rawは書き換えない。
2. **構造化英字パターン判定を非正規表現化** (`CompositionDetector.Segment`)。毎打鍵で `Regex.Matches(token, "[a-z][A-Z][a-z]").Count >= 2` を作成していた箇所を、重ならない3文字一致の直接走査へ置換した。非重複一致という元の正規表現の条件を維持。
3. **笑いの `w` ラン分類を線形時間で前処理** (`CompositionDetector.Prepare`)。各位置から後方に連続するASCII英字を調べ直す処理を後方累積情報で置換。表示用英語区間のスコアや判定閾値は変更しない。
4. **回帰テストを追加** (`NativeAcronymNormalizationTests`)。大文字略語・後続日本語・Rawの無損失性・非重複3文字パターンの契約を明示。

## 検証結果と制約

- アルゴリズムの参照実装として、変更前後の `w` 判定および正規表現相当パターン判定を、各**固定seedの100,000件のPython生成入力**で比較した。比較上の差異は0件。
- これらは **C#コードやIMEの実行結果ではない**。この作業環境には .NET 10 SDKがなく、MeltiaのGitHub Actionsも実行枠の問題により使用できないため、C#のビルド・全回帰・実機性能は未検証。
- 今回の変更は `commit`、`meltiawotamesu`、`japaneseno` 等の一般的な日英境界精度の解決を保証しない。特に研究版v1.0-devはまだ実IMEへ接続していない。

## ローカルで必要な試験 (.NET 10 SDK)

```powershell
dotnet run --project src/Meltype.Core.Tests -- NativeAcronymNormalizationTests
dotnet run --project src/Meltype.Core.Tests
dotnet run --project src/Meltype.Core.Tests -- --segments IME
dotnet run --project src/Meltype.Core.Tests -- --repro commit enter
dotnet run --project src/Meltype.Core.Tests -- --repro meltiawotamesu enter
dotnet run --project src/Meltype.Core.Tests -- --repro japaneseno enter
dotnet run --project src/Meltype.Core.Tests -- --repro tokyo enter
```

上記に加えて、Windows実機でTSF中の未確定表示・変換確定・BackSpace・再変換・高速連打・p50/p95/p99を測り、旧版との比較結果を保存すること。**CI成功または同等のローカル検証結果が得られるまではPRをDraftに留め、mainへはマージしない。**

## 次の設計段階

現行の `CompositionText` は打鍵時にローマ字をかな単位へ正規化し、その単位を前提に `CompositionDetector` が区間探索する。一方、Meltia Coreは元のRawを基準とするUTF-16半開区間を返す。この差が `commitshita` のような「英語末尾と日本語先頭が一つのローマ字単位になる」ケースで重要となる。

次の改修では、**Rawを唯一の座標系**として (1) 語候補と明示ロック、(2) 日本語音韻候補、(3) 境界探索・不確実性の診断、(4) 表示・確定用の再レンダリングを分離する。まず読み取り専用のshadow経路でネイティブとMeltia Coreの境界を比較する。候補を見つけただけで確定へ流さず、UTF-16位置と生文字列の完全一致・保護区間優先・負荷上限を品質ゲートにする。
