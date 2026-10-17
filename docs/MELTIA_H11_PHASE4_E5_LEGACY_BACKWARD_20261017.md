# H11 Phase 4 / PR-E5 — 従来Meltia Core後ろ向きTop-Kの互換アダプター（2026-10-17）

> E4 [PR #49](https://github.com/Fugu0141/Meltia/pull/49) を起点とする**積み上げ依存PR**。E3→E4→E5の順にレビュー・マージし、後続PRのbaseをmainに更新してからCIを再確認する。既定 `MeltiaBoundaryEngine.Analyze` / Windows Preview / TSF は切り替えない。

## 1. 要件・非対象

- **移行するもの**：従来Coreの `BuildRoutes`（候補ラティスを終端から先頭へ走査するシンプルなTop-K後ろ向きDP）を、E4の `IBackwardOfferProvider`／`IBackwardRouteRetention`／`BackwardLatticeDecoder` に適合。新メソッド `AnalyzeWithSharedBackward` は内部限定。
- **旧エンジンとv0.8の違い**：従来Coreのスキップ経路はmandatory内では不可。候補は `byStart` にまとめ、mandatory始点に一致する候補のみ許可。スコアは `edge.Score + tail.Score - perSpanCost`。保持は `OrderByDescending(Score)` → 四桁ゼロ埋め start/end の ordinal tie → 英語区間署名による `DistinctBy` → `Take(8)`。**v0.8の英語区間数層代表選抜は適用しない。**
- **非対象**：候補生成と字種スコア・辞書、全optionsの意味、`MeltiaPhoneticLatticeDecoder`、H10/H11、Preview既定経路、既存 `BuildRoutes` の削除、正式IME、Release変更。
- **安全性**：従来の入力保存・自動確定不可、明示EN/JA・Protected・Meltype英語の優先順位、URL-first、UTF-16、`MaxCandidates=128`、`MaxRoutes=8` の契約を維持。

## 2. 基本設計

1. `Analyze` は既存の検証・候補生成と同じ `AnalyzeCore` を使い、フラグの既定値を`false`にして従来 `BuildRoutes` をそのまま呼ぶ。新比較入口 `internal AnalyzeWithSharedBackward` のみ同一候補・hard・perSpanCostを新後向きアダプターへ供給する。
2. `LegacyBackwardOffers` は mandatory始点判定・内部ブロック・候補ごとの尾部の経路展開を担当。 `LegacyBackwardRetention` は従来の比較キーとDistinctBy・幅8を同順で適用する。
3. 新後向き基盤は位置の逆走査のみを受け持ち、辞書・文脈・候補言語・スコア規則は知らない。E4のv0.8選抜とE5の単純Top-K選抜は異なるRetention実装。
4. Frozen 103件とseed付き混在・UTF-16長文・ロック・URL・メール・null/例外を比較する。全候補・全ルート・スコア・Truncated・Reviewの各フィールドを検証。自動確定は禁止のまま。

## 3. 品質ゲート

- 初回は既存 `BuildRoutes` を残すため、実験用フラグ経路の一致を検証しても本番採用可否は別。新旧経路を同一入力・同一Evidence・同一optionsで比較し、Top1以外のTop-K、score、重複・同点、lock例外、Reviewの全フィールドも確認。
- .NET10 Release Core、H11 103 fingerprint、E3/E4同値、H10、strict-quality、Windows Preview無フックを通す。最終結果と失敗事例をこの文書へ追記。
- E1〜E5の構造的な候補生成/Decoder/射影準備は、実験用の並列経路が成立したという意味のみ。**実際の通常Analyzeを共通Decoderへ切替える性能・OS E2E・自然文holdoutの採用ゲートは別フェーズ**。次期H12機能と新スコアは要件定義前に導入しない。

## 4. 実装・結果

- PR [#50](https://github.com/Fugu0141/Meltia/pull/50) に `MeltiaBoundaryEngine.E5Adapters.cs`、比較用 `AnalyzeWithSharedBackward`、E5専用回帰テストを追加。旧Core `BuildRoutes` の本体は変更せず、`Analyze` は既定の旧経路を維持する。
- [Core boundary diagnostics / Actions #39000836001](https://github.com/Fugu0141/Meltia/actions/runs/39000836001) **SUCCESS / ALL PASS 262**。E5追加6契約：研究オプション・URL/メール・強制ロック・曖昧性Review、固定103 raw、seed混在125、UTF-16長32/128/256/512/1024、重複/交差ロックと最大長の例外型・メッセージ、連続呼出の状態分離を確認。
- 最新の**コードHEAD `f033c6bb9c00a6df6ff0851544344e1d64a6f8df` の8種類のActions（Core・H10比較・H11 Phase1 Trace・合成ベンチ・境界品質・Windows Preview無フック・strict-quality・PR checks）はすべてsuccess**。Windows CIは同HEADでPreviewのpublish、smoke、1,067件研究品質ゲートの成功を報告した。ただしWindows**実キー入力E2E**を意味しない。
- 新後向き比較器の結果は既存の `BuildRoutes` に対する**契約一致**を示す。既定Core/H11/H10の新Decoder切替・速度保証・本番IME動作・独立holdout精度改善を主張しない。
- E3 [PR #48](https://github.com/Fugu0141/Meltia/pull/48) → E4 [PR #49](https://github.com/Fugu0141/Meltia/pull/49) → E5 [PR #50](https://github.com/Fugu0141/Meltia/pull/50) は依存関係の順にレビューしてからMergeし、各Merge後に後続のbase更新・再CIを実施する。Phase4の既定経路への採用可否は [移行完了判定ゲート](MELTIA_H11_PHASE4_MIGRATION_EXIT_GATES_20261017.md) を別途満たして判断する。
