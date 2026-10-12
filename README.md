# Meltia

**Meltia** は、[Meltype](https://github.com/yksr-melt/Meltype) を基に開発する、非公式・独立運営の日本語入力ソフトウェアプロジェクトです。日本語・英語が混在する入力の自然さと、IME の安定性の向上を目指します。

> **現在は開発準備段階です。** このリポジトリのソースコードや実行ファイル内には、まだ `Meltype` の名前・識別子・インストール設定・自動更新設定が残っています。Meltia 独自の正式リリースはありません。本家の配布物と混同せず、現時点のビルドを日常用の Meltia としてインストールしないでください。

## 開発目標

- **安定性** — 再現可能な不具合報告、回帰テスト、CI に基づく改善
- **自然な日英混在入力** — 日本語と英語の境界を逐次推定し、誤変換を減らす
- **IME としての完成度** — 入力・確定・取り消し・学習・辞書との連携を検証
- **検証可能な改善** — 精度だけでなく誤判定の種類、遅延、安定性も記録

境界判定方式は [IncrementalBoundaryLab](https://github.com/Fugu0141/IncrementalBoundaryLab) で研究中です。独立したMeltia Coreでは、v0.5の基盤に続き、**[v1.0-devの不確実性レビュー](docs/MELTIA_V10_UNCERTAINTY_REVIEW_20261012.md)を研究用オプションとして実装**（v0.7候補生成・v0.8音韻ラティス・v0.9経路評価は維持）しています。[Meltypeとの比較アダプター](docs/MELTIA_SHADOW_COMPOSITION_BRIDGE.md)は読み取り専用です。**実際のIMEの入力・自動変換には未導入**で、Windows TSF上の実測精度・安定性も未検証です。

## 現在の状態と今後

| 項目 | 状況 |
| --- | --- |
| Meltype からのソース・Git 履歴の継承 | 完了 |
| Meltia 用の説明・ライセンス方針 | 整備中 |
| アプリ名・IME 登録・インストール先・自動更新の分離 | 未実施 |
| Meltia 専用の安定版リリース | 未実施 |
| 新しい境界判定アルゴリズムの統合 | 独立Coreはv1.0-dev研究段階、実入力への統合は未実施 |

詳細は [開発ロードマップ](docs/MELTIA_ROADMAP.md) を参照してください。

## ドキュメント

- [プロジェクトの由来と本家との関係](docs/MELTIA_ORIGIN.md)
- [ライセンスと著作権の取り扱い](docs/MELTIA_LICENSE_POLICY.md)
- [開発ロードマップ](docs/MELTIA_ROADMAP.md)
- [開発バージョン管理（v0.5〜v1.0-dev）](docs/MELTIA_VERSIONING.md)
- [v0.7 未知英語候補生成の実験・検証](docs/MELTIA_V07_UNKNOWN_LATIN_20261012.md)
- [v0.8 音韻ラティス・経路探索の実装と実測](docs/MELTIA_V08_PHONETIC_LATTICE_20261012.md)
- [v0.9 複数英語区間の文脈的評価と追加290件の比較](docs/MELTIA_V09_CONTEXTUAL_ISLANDS_20261012.md)
- [v1.0-dev 境界の不確実性レビューと867件評価](docs/MELTIA_V10_UNCERTAINTY_REVIEW_20261012.md)
- [v1.0 フェーズ2：96文字を超える日英混在入力の候補生成研究](docs/MELTIA_V10_LONG_RUN_PHASE2_20261012.md)
- [v1.0 フェーズ3：候補上限128件での位置的多様性と1019件評価](docs/MELTIA_V10_DIVERSE_CANDIDATES_PHASE3_20261012.md)
- [v1.0 フェーズ4：日本語吸収を抑制するTop1経路スコアリング・1067件評価](docs/MELTIA_V10_FLANK_RANKING_PHASE4_20261012.md)
- [貢献方法](CONTRIBUTING.md)
- [セキュリティ報告](SECURITY.md)
- [サードパーティー通知](THIRD-PARTY-NOTICES.md)
- [開発・ビルド手順（本家から継承、Meltia 用に未更新）](docs/DEVELOPMENT.md)

## 開発者向け

現時点のソリューション名は **`Meltype.sln`** のままです。開発環境が整っていれば、例えば次のコマンドで既存コードのビルドを試せます。

```powershell
dotnet build Meltype.sln -c Release
```

Windows の IME コンポーネントや配布パッケージのビルドには別途ツールが必要です。詳細は継承した [開発文書](docs/DEVELOPMENT.md) を確認してください。既存の `docs/USAGE.md`・`docs/RELEASE.md`・スクリプトは **本家 Meltype 向けの記述を含む** ため、Meltia の利用・配布手順としてそのまま用いないでください。

## 原著作権・ライセンス

本プロジェクトは [yksr-melt/Meltype](https://github.com/yksr-melt/Meltype) の派生版で、**本家の公式版・後継版ではありません**。本家の作者・貢献者による承認や保証を意味しません。元の開発者、貢献者、ロゴ制作者などへのクレジットと履歴は [本家リポジトリ](https://github.com/yksr-melt/Meltype) および Git 履歴から確認できます。

プログラム本体は [GNU GPL v3 またはそれ以降](LICENSE)（`GPL-3.0-or-later`）に従います。MIT への一括変更は行っていません。辞書や同梱コンポーネントには別のライセンスがあるため、[ライセンス方針](docs/MELTIA_LICENSE_POLICY.md) と [サードパーティー通知](THIRD-PARTY-NOTICES.md) をあわせて確認してください。

Meltype のロゴ・名称を Meltia の独自ブランドや公式承認の表示として流用する予定はありません。現在残る本家の画像等は継承した資料として扱い、対外的な配布前に権利と利用条件を確認します。
