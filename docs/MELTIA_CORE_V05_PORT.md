# Meltia Core v0.5 — 初回リポジトリ統合（実験機能）

## 変更の位置付け

既存のMeltype由来アプリを変更せず、独立した新しい境界候補エンジンを
`src/Meltia.Core/` に配置した。現在は **シャドー診断専用**であり、
既存の`CompositionDetector`、`CompositionController`、TSF、Mozcによる
漢字変換やテキスト確定を置き換えない。

これまでのv0.5研究で重視してきた安全条件をC#へ移すための **初回の
実装統合**。過去ZIPに含まれていた全Python研究コード・評価済み辞書・
既存のv0.1〜v0.5 C#参考実装をそのまま丸ごと移植したものではない。
Python/C#の完全同等性も未検証である。

## 含めた実装

- `MeltiaBoundaryEngine`: 区間候補の統合・強制区間・Top-K経路（最大8）。
- `MeltiaRomaji`: 日本語ローマ字として成立するかのDP評価。日本語らしい
  連続入力中の短い英語辞書一致を自動採用しない。
- `MeltiaSession`: 文字追加・途中編集・削除・UTF-16キャレット検証。
- `Meltia.Cli`: 生のUTF-16座標、根拠、候補経路、未確定フラグをJSON表示。
- `Meltia.Core.Tests`: 日本語誤分割・日英混在・複数名前・ユーザー指定・
  URL/メール保護・編集・補助候補上限のスモーク契約テスト。
- 単独.NET 10ビルドとテストのGitHub Actions。

`BoundaryEvidence` は辞書語・固有名詞・既存Meltypeの英語区間・明示英字/
日本語指定・保護区間を受け取る。**辞書ヒットや既存Meltypeの判定だけを
根拠に自動確定しない**。すべての`BoundaryResult`で
`AutoCommitAllowed == false`。

## 動かし方

.NET 10 SDK が必要。

```sh
dotnet build src/Meltia.Cli/Meltia.Cli.csproj -c Release
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project src/Meltia.Cli/Meltia.Cli.csproj -- handanwodelegatesuru --word=delegate
dotnet run --project src/Meltia.Cli/Meltia.Cli.csproj -- kyouhaKingGnudeSPYAIRde
dotnet run --project src/Meltia.Cli/Meltia.Cli.csproj -- tokyo --latin=0:5
```

生の`tokyo`が英語か日本語かは打鍵情報だけでは一意に決められない。
`--latin`を指定した場合だけ明示意図として優先する。

## 今後の統合手順

1. GitHub Actionsの.NETビルドと契約テストが緑であることを確認する。
2. 研究コーパス（H系・P1系）に対するC#実測比較を追加する。
3. `Meltype.Composition.CompositionSegment`を`BoundaryEvidence.MeltypeEnglish`
   に写す読み取り専用アダプターを加える。
4. 既存変換結果と新しい候補経路を**シャドー状態で**比較し、
   遅延・境界F1・正解経路・日本語誤選択を別々に計測する。
5. Windows TSF / Mozc / ユーザー辞書と連携する前に、C#とPythonの一致、
   編集後の安定性、実際のキー入力p95/p99を確認する。

**この段階で本番IMEの精度向上やWindows上の動作を主張しない。**
リポジトリに実装ソースを置き、継続的にテスト・改善できるようにするのが目的。

## ライセンス

既存リポジトリのGPL-3.0-or-laterに合わせてソースを配置する。
実験コードの追加により、再利用しているMeltype由来コードのライセンスは変更されない。
