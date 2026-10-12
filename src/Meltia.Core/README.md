# Meltia Core — Independent boundary candidate engine

独立C#プロジェクト。日本語ローマ字の保護、英語候補の保留、Top-K境界探索、
明示指定、UTF-16安全性を実装した研究用モジュールです。

現在は既存MeltypeのIMEの動作へ接続していません（**シャドー専用**）。
本番変換の既定値や自動確定には一切使用しません。

実験機能は **Meltia v0.8**（v0.7の未知英語候補生成に加え、日本語音韻経路を使う独立デコーダ）まで実装されています。
既定では無効で、正式版の意味ではありません。研究番号 `H1`〜`H9` とは別管理です。

詳細:
- [開発バージョン管理](../../docs/MELTIA_VERSIONING.md)
- [v0.5 初回Core統合](../../docs/MELTIA_CORE_V05_PORT.md)
- [v0.7 未知英語候補生成](../../docs/MELTIA_V07_UNKNOWN_LATIN_20261012.md)
- [v0.8 日本語音韻ラティスによる経路比較](../../docs/MELTIA_V08_PHONETIC_LATTICE_20261012.md)

```sh
dotnet run --project src/Meltia.Cli -- kyouhaKingGnudeSPYAIRde
dotnet run --project tests/Meltia.Core.Tests
```
