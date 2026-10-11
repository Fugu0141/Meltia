# Meltia Core — Independent boundary candidate engine

独立C#プロジェクト。日本語ローマ字の保護、英語候補の保留、Top-K境界探索、
明示指定、UTF-16安全性を実装した研究用モジュールです。

現在は既存MeltypeのIMEの動作へ接続していません（**シャドー専用**）。
本番変換の既定値や自動確定には一切使用しません。

詳細: [Meltia v0.5の初回統合説明](../../docs/MELTIA_CORE_V05_PORT.md)

```sh
dotnet run --project src/Meltia.Cli -- kyouhaKingGnudeSPYAIRde
dotnet run --project tests/Meltia.Core.Tests
```
