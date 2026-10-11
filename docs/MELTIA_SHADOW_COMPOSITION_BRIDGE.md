# Meltia v0.5 — Meltype境界の実装連携（読み取り専用）

このPRは、既存の`src/Meltia.Core/Boundary` にあるC#境界候補エンジンを、**実際の** `Meltype.Composition.CompositionSegment` と接続するアダプターを導入します。

## 配置

- `src/Meltia.CompositionBridge`: Meltype.Core + Meltia.Coreへのプロジェクト参照。既存アプリのプロジェクトからは参照しません。
- `MeltypeBoundaryShadowBridge.FromSegments`: 実際のMeltype分割のRawを検証し、英語区間・保護区間をUTF-16半開区間へ変換。
- `CompareSegments`: P1/H系の知見による保留候補とMeltypeの分割候補を別々に保存し、最上位経路の一致を診断。
- `CompareDetector`: `CompositionDetector.Segment` をオフラインで呼び、その結果を読み取り専用で比較。
- `tests/Meltia.CompositionBridge.Tests`: 日英混在、2固有名詞、`tokyo`、編集向け原文座標、絵文字、URL、ユーザーロック、誤った分割の棄却。
- `.github/workflows/meltia-composition-bridge.yml`: .NET 10のビルドと橋渡しテストをGitHub Actionsで実行。

## 安全方針

- **自動確定しません。** `BoundaryResult.AutoCommitAllowed` と `BoundaryShadowComparison.AutoCommitAllowed` は常にfalse。
- **本番TSF・Meltypeの`CompositionController`・学習履歴・Mozc変換は変更しません。**
- Meltypeが提示した英語区間は、既定では「証拠」でありユーザーの英語固定ではありません。曖昧な日本語ローマ字を辞書ヒットだけで英字へ自動変更しません。
- `IsProtected` は強制保護候補として扱い、ユーザーの日本語固定と矛盾する場合は例外を返します。
- 原文不一致、文字列未カバー、サロゲートペアの中間での境界は例外。推測して座標を修復しません。
- 比較結果と原文はメモリ内で返すだけです。入力内容の外部送信やファイル保存、テレメトリーは追加しません。

## 依然として未実装・未検証

- このブリッジの本番ホスト接続・IME入力イベントとの結線
- MeltypeとMeltiaのH系/P1系全コーパスでの完全同等性
- 実入力のp95/p99レイテンシ、Windows TSFでの長時間稼働
- v0.5 ZIPの全研究コードとC#参考コードの完全移植（このPRは既存のMeltia Coreの**実装連携**を進めるもので、全機能置換ではありません）
- 本家Meltypeより精度が高いという主張

## 手元またはCIで実行

```bash
dotnet build src/Meltia.CompositionBridge/Meltia.CompositionBridge.csproj -c Release
dotnet run --project tests/Meltia.CompositionBridge.Tests/Meltia.CompositionBridge.Tests.csproj -c Release
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
```

**採用基準**: CIグリーン、回帰ゼロ、実H/Pデータの同条件比較、Meltiaの独立アプリID確認の後、OSに影響しないシャドー診断から導入。今は既定IME変換への採用を禁止。
