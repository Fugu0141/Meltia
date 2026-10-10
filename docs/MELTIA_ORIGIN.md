# Meltia の由来と本家との関係

## 位置づけ

- **Meltia**: [Fugu0141/Meltia](https://github.com/Fugu0141/Meltia)。本家から独立して運営する非公式の派生プロジェクト。
- **上流プロジェクト**: [yksr-melt/Meltype](https://github.com/yksr-melt/Meltype)。
- **研究プロジェクト**: [Fugu0141/IncrementalBoundaryLab](https://github.com/Fugu0141/IncrementalBoundaryLab)。境界判定の研究用リポジトリであり、Meltia 本体とは別に管理する。

Meltia は Meltype の公式リリースや公式サポートを代行するものではありません。本家の商標・ロゴ・対外的な信用を独立版の保証として扱いません。

## ソースコードの来歴

2026-10-11（日本時間）の初期整備時点で、Meltia の `main` は Meltype 本家のコミット `cc456a00521ae6143680196adf0f0c4b96d131ae` を起点にしています。

```text
Upstream : yksr-melt/Meltype  (main)
Derivative: Fugu0141/Meltia   (independent repository)
Research : Fugu0141/IncrementalBoundaryLab
```

初期取り込みでは Git のコミット履歴を引き継いでいます。既存ファイルに記載された著作権・SPDX 表示、[LICENSE](../LICENSE) および [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md) は維持します。元の README の内容は本家リポジトリと取り込み元のコミット履歴で参照できます。

## 上流変更の取り込み

Meltia は独立リポジトリのため、本家の変更が自動的に適用されるわけではありません。必要な修正を選び、ライセンス・ビルド・テスト・Meltia 独自部分との互換性を確認して取り込みます。

```bash
git remote add upstream https://github.com/yksr-melt/Meltype.git
git fetch upstream
# 取り込み前に差分・ライセンス・Meltia 固有の設定を確認する
```

すでに `upstream` が登録されている場合、`git remote add` は不要です。上流の変更を無条件で `main` にマージしないでください。特にインストーラー、IME 登録、アプリ識別子、アップデーター、GitHub Actions は混同や上書きの原因になります。

## クレジット

Meltype のオリジナル作者は **雪代 / Yukishiro（[@yksr-melt](https://github.com/yksr-melt)）** です。元のソースにはその他多数の貢献者の成果が含まれています。コードの作者情報と履歴を保持し、Meltia の変更は別途明記します。

上流の README に記載されたロゴ制作者 **[@Crysta1221](https://github.com/Crysta1221)** および各貢献者のクレジットも尊重します。画像・ロゴの利用権はプログラム本体の GPL と同一とは限らないため、Meltia ブランドへの転用を前提にしません。
