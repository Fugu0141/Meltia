# H11 Phase 4 / PR-E2 — 候補生成器とTop-K経路射影の分離（2026-10-17）

> **状態：E2研究用・レビュー待ち。** 起点はE1 PR #46統合後のmain `549a0862b51223bc9795cb1be285a68c36d4730a`。上位要件は [Phase 4改善計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) と [E1契約](MELTIA_H11_PHASE4_E1_FORWARD_DECODER_20261017.md)。

## 1. 要件と対象外

- **目的**：H11の候補生成をモデル所有の `IForwardCandidateGenerator<TRequest,TEdge>` に分離し、共通前向き探索の出力を `IForwardRouteProjector<TNode,TEdge,TResult>` でTop-Kに射影可能にする。H12等が新探索ループや出力処理を丸ごと複製しない構造を用意する。
- **互換契約**：旧H11の保護ロック／URL・メール／UTF-16／生成順と重複／算術スコア／状態順・ビーム発火／同点・最終候補ソート／英語隣接結合・署名dedup／Top8／不確実性／例外種別／Trace順序を保持する。候補の列挙は**始点バケット順**（全体的な生成時刻順ではない）。
- **採用範囲**：従来 `Analyze` と `AnalyzeWithTrace` は旧H11前向き探索と旧経路射影を維持。E1 `AnalyzeWithSharedDecoder*` も既存の経路射影を維持。E2の内部限定 `AnalyzeWithPipeline*` のみ共通Decoder＋新射影を通す。候補生成器は全経路で同じものを使用し、E2のリファクタリングによる意味差をPhase0オラクルで検出する。
- **非対象**：辞書更新、重み調整、曖昧性解消、候補削減／重複排除、H10・v0.8移植、既定H11探索の新Decoder切替、Windows Preview／TSF／IME接続、実機検証。研究用 `AutoCommitAllowed=false` を保持する。

## 2. 基本設計

1. **候補生成**：事前ロック検証は元の `AnalyzeCore` に保持し、検証済みraw・hard・明示JA・任意Traceをモデル内部リクエストにまとめる。現H11の `Add`／構造語／音韻／語彙／数字接頭語のコードをそのまま単一 `BuildCandidateEdges` メソッドに抽出する。`H11CandidateGenerator` がバケット群を持つ `H11OrderedCandidates` を構築する。`At(position)` は旧 `edges[position]` と同じ参照/順序を返し、`InStartPositionOrder()` は元の `edges.SelectMany` に一致する。
2. **探索**：通常のH11ループは変更しない。E1/E2の明示的な実験経路だけ `ForwardLatticeDecoder.Decode` を使う。新インターフェースは状態/スコアやH11特有の読みの意味を共通ライブラリに持ち込まない。
3. **経路射影**：E2の `H11RouteProjector` が、最終位置のJA→EN→Neutral結合、`OrderByDescending(Score).Take(48)`、`Stack<Edge>` 復元、連続EN統合、`start:end` 署名重複排除、最大8ルート、Trace `Selected` と `Finalists`、差分1.5未満の `NeedsConfirmation` と `Analysis.Edges` を旧コードと同じ演算・順序で実装。汎用インターフェースの責務は「最終状態からモデル固有の観測出力を構築」のみ。
4. **責務分離の理由**：v0.8の後向きDPとH10の異なるビーム契約をこのPRに混ぜない。新ルート射影を公開製品APIにしない。Traceでのフィールド・順序の微差も互換性違反として扱う。

## 3. 受け入れ条件／検証計画

- E2新経路・E1経路・旧 `Analyze` の**同一入力/同一Evidence**の全候補順・origin・スコア、全Top-K英語区間/score、不確実性・自動確定禁止の同値。
- Phase0の103件は旧H11の独立凍結fingerprint試験と、E2との逐次同値試験の両方を実施。Traceは候補・採否理由・ビームDrop順序・経路step・丸め残差まで比較。
- 明示JA/EN/Protected・URL/メール・Unicode/サロゲート・例外・空入力・最大1024 UTF-16・固定seed混在raw・再実行を検証する。Goldラベルを推論に渡さない。
- Core Release、H11固定103、Trace、H10、Windows Preview無フック・strict-qualityのGitHub Actionsを確認する。環境のないローカルで「テスト実行済み」とは扱わない。
- 新経路の性能改善や精度向上は主張しない。採用判断前に同一runnerのフルベンチとWindows入力の独立評価が必要。レビュー完了までマージしない。

## 4. 検証記録

- 実装の結果とCIのURL・未解決点はPRレビュー時に追記する。現時点ではE2を製品に接続しない。
