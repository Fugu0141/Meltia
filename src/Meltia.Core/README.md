# Meltia Core — Independent boundary candidate engine

独立C#プロジェクト。日本語ローマ字の保護、英語候補の保留、Top-K境界探索、
明示指定、UTF-16安全性を実装した研究用モジュールです。

現在は既存MeltypeのIMEの動作へ接続していません（**シャドー専用**）。
本番変換の既定値や自動確定には一切使用しません。

実験機能は **Meltia v1.0-dev**（v0.7候補生成、v0.8音韻デコーダ、v0.9経路加点に、不確実性レビューを追加）まで実装されています。
既定では無効で、正式版の意味ではありません。研究番号 `H1`〜`H9` とは別管理です。

詳細:
- [開発バージョン管理](../../docs/MELTIA_VERSIONING.md)
- [v0.5 初回Core統合](../../docs/MELTIA_CORE_V05_PORT.md)
- [v0.7 未知英語候補生成](../../docs/MELTIA_V07_UNKNOWN_LATIN_20261012.md)
- [v0.8 日本語音韻ラティスによる経路比較](../../docs/MELTIA_V08_PHONETIC_LATTICE_20261012.md)
- [v0.9 助詞を挟む複数英語区間の経路評価](../../docs/MELTIA_V09_CONTEXTUAL_ISLANDS_20261012.md)
- [v1.0-dev 境界の不確実性レビュー](../../docs/MELTIA_V10_UNCERTAINTY_REVIEW_20261012.md)
- [v1.0 フェーズ2：長文の未知英語候補生成（既定OFF）](../../docs/MELTIA_V10_LONG_RUN_PHASE2_20261012.md)
- [v1.0 フェーズ3：128候補予算の位置的多様化（既定OFF）](../../docs/MELTIA_V10_DIVERSE_CANDIDATES_PHASE3_20261012.md)

```sh
dotnet run --project src/Meltia.Cli -- kyouhaKingGnudeSPYAIRde
dotnet run --project tests/Meltia.Core.Tests
```
