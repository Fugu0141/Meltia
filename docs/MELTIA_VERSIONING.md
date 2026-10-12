# Meltia 開発バージョン管理

## ルール

**Meltia 本体の開発版**には `v0.x` を使用する。`H1`〜`H9` は [IncrementalBoundaryLab](https://github.com/Fugu0141/IncrementalBoundaryLab) の実験仮説番号であり、Meltiaの開発版番号ではない。製品の追加機能を誤って `H10` と呼ばない。

- **v0.5**: 独立 C# Boundary Core。UTF-16区間、Top-K、必須保護、編集・シャドーブリッジの基盤。
- **v0.6**: 境界スコア・ランキングの実験的改良。辞書条件を揃えた47ケース・530ケース評価、過剰分割対策、Meltype参考候補の再評価、失敗段階の測定。2026-10-12の[v0.6相当の検証記録](MELTIA_RANKING_EXPERIMENT_20261012.md)。
- **v0.7**: 未知英語の候補生成と複数英語区間の候補Recall改善。以前に誤って「H10」と表記した実装・テスト・評価の正式な開発版名称。[v0.7実装・検証記録](MELTIA_V07_UNKNOWN_LATIN_20261012.md)。
- **v0.8**: v0.7の候補生成器を残し、日本語の音韻経路も同時探索する選択可能な境界デコーダ。[v0.8設計・検証記録](MELTIA_V08_PHONETIC_LATTICE_20261012.md)。
- **v0.9**: v0.8の経路探索に、短い日本語助詞を挟む2つの英語候補の**文脈的な経路加点**をオプトインで追加。候補リストは変更しない。[v0.9評価記録](MELTIA_V09_CONTEXTUAL_ISLANDS_20261012.md)。

この表は**開発上の実装段階**を区別するためのもの。GitHubのタグやリリースが存在することを示すものではなく、いずれも**Meltia独立IMEの正式リリース済みバージョンではない**。

### v0.9の技術的な状態

- `Meltia.Core` の開発版番号は **`0.9.0-dev`**。アプリ・IMEは更新していない。
- `ExperimentalContextualIslandScoring` は既定OFF。v0.8の音韻ラティス・v0.7未知英語候補が有効な条件でのみ意味を持つ。
- [Draft PR #8](https://github.com/Fugu0141/Meltia/pull/8) はv0.8のDraft PR #7をベースとする、分離した差分。mainには未マージ。
- 47件・530件・別語彙290件の**合成テスト**でv0.8と比較し、候補集合不変、Top1/Top-K非退行、合成日本語のみ負例をCIで監視。自然文精度は未検証。

### v0.8の技術的な状態（歴史的な記録）

- `src/Meltia.Core/Meltia.Core.csproj` のv0.8当時の開発版は **`0.8.0-dev`**。Meltype本体やインストーラーの版は更新しない。
- `ExperimentalPhoneticLatticeDecoder` は既定OFF、評価用途のみ。v0.7方式は同じCore内に残し比較可能にする。
- このDraft PRは [#7](https://github.com/Fugu0141/Meltia/pull/7)。**ベースはDraft PR #6の開発ブランチであり、mainではない**。
- 日本語のみの誤英字選択、Gold候補検出率、Top-K、Top1、処理負荷を実測してから採用判断する。

### v0.7の技術的な状態（歴史的な記録）

- `src/Meltia.Core/Meltia.Core.csproj` の開発版メタデータを **`0.7.0-dev`** とし、v0.7開発時点ではCoreテストで`0.7`を検査した（v0.8側では`0.8`へ更新）。これはWindowsのMeltypeホストやインストーラーのバージョンではない。
- 研究用スイッチ `ExperimentalUnknownLatinCandidates` で有効化する。既定値は無効。
- 候補に格納する `Origin` は `v0.7-unknown-latin-provisional`、診断モデル名は `Meltia-v0.7-*`。機能識別子とバージョン名を一致させる。
- 既存の候補と得点、UTF-16契約、IME本番の入力処理は変更しない。
- `v0.7` の **Top1実入力の精度が完成したわけではない**。未知英語を候補として列挙できるが、候補過剰生成とTop1選択は未解決。
- GitHub PR [#6](https://github.com/Fugu0141/Meltia/pull/6) は開発用Draft PR。古い `feat/v05-...` ブランチ名は初期作業の由来を表すもので、現在の機能バージョンを表すものではない。正式採用前にPR分離・レビューを検討する。

## バージョンの採用方針

1. 実験の設計と評価には研究仮説番号を使ってもよいが、Meltiaのコード・CI・ドキュメントの開発版名は常に`v0.x`を使う。
2. 候補検出率、Top-K、Top1、辞書の利用条件を区別して記録する。研究済み合成コーパスだけで精度保証を宣言しない。
3. 未公開実験のため、コード内のユーザー入力に影響しない診断名は変更可能。ただし過去のコミット・CI証跡・報告数値は改変せず、旧名称が現れたら履歴として注記する。
4. **GUI・IMEの実働版実装は、境界判定の改良・検証を済ませてから別タスクとして進める。**
