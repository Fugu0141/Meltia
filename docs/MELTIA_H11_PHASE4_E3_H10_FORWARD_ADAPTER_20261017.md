# H11 Phase 4 / PR-E3 — H10前向き共通Decoderアダプター（2026-10-17）

> **設計・実装中。** 起点 `5000f27732f58cd4c88cbe93cc7bb12cf5584518`（E2 PR #47 統合済みmain）。上位方針は [Phase4改修計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) と [E1](MELTIA_H11_PHASE4_E1_FORWARD_DECODER_20261017.md) / [E2](MELTIA_H11_PHASE4_E2_GENERATOR_PROJECTION_20261017.md)。このPRはH10の**内部実験経路だけ**が共通Decoderを使用する変更であり、H11の既定動作、Windows Preview、TSFは変更しない。

## 1. 要件定義

- **移行対象**：`H10BoundaryPrototype` の位置×言語状態の**前向き**有限ビームDP、H10モデル固有の候補生成／遷移算術／ビーム保持／経路射影をE2の汎用契約に適合させる。デフォルト `Analyze` は旧H10検索と旧射影を保持し、独立比較オラクルとする。
- **絶対互換**：raw、生成候補の全順序・重複・origin・score、全Top-Kのspan/score、NeedsConfirmation、AutoCommitAllowed、強制EN/JA/Protected、URL/email、UTF-16 surrogate、最大長1024、例外型／主なエラーメッセージ。入力正解ラベルは推論に使わない。
- **H10固有（H11と混同しない）**：探索幅8、各終点言語状態が**24件を超えたら8件へソート剪定**、状態遷移 `-2.0`、Neutralは直前言語維持、候補の順序は構造語→start順mandatory→literal/JA FST/EN spelling。最終候補 `Take(24)`→英語スパン署名でdedup→最大8。2位との差`<1.5`かつ英語スパン列が異なる場合のみ確認要。
- **H11/従来Core/v0.8はこのPRで改変しない**。v0.8の逆向き探索と区間数別ルート多様性は前向きDPと異なるため、移植ではなく別紙の不変条件と比較方針を策定する。

## 2. 基本設計と危険箇所

1. 既存 `AnalyzeCore` でraw長・BoundaryEvidence・全ロック・URL優先順位と重複の検証を継続し、既存の候補生成コードを**計算順序・元のローカル型を変えず** `BuildCandidateEdges` に抽出。`H10CandidateGenerator` と `H10CandidateGraph` は `List<Step>[]` の具体型を保持し、既定の旧探索は従来の直接List列挙・`ToArray()`を残す。
2. `H10ForwardTransition` は旧ループ内の外側状態 `previous` とノード `State.Last` が同値である不変条件を使う。状態の不一致が発生すれば例外で停止して不正経路を黙認しない（共通Decoderのstateを暗黙に再ラベルしない）。浮動小数点加算を `state.Score + edge.Score + boundaryPenalty` と正確に保持する。
3. `H10ForwardBeam` の `PruneAboveCount=24`、`Sort((a,b)=>b.Score.CompareTo(a.Score))`、`RemoveRange(8, count-8)` を保持。安定ソート・beamの多様性保持は導入しない。
4. `H10RouteProjector` はH10専用ルート射影を元のアルゴリズムのまま分離。通常 `Analyze` とE3の `AnalyzeWithSharedPipeline` は異なる検索・出力処理を通すが、候補生成だけ共通。
5. 明示実験メソッドは `internal`。今回のCI結果だけで旧H10を削除したり通常経路に切り替えたりしない。

## 3. 受け入れ・比較ゲート

- 103件の固定Issue入力（47+56）を両H10経路へ独立に入力し、ラベルを渡さず**全候補・全Top-K・スコア・確認要フラグ**を完全比較。
- URL/email、英日/Protectedの複数ロックと重複・矛盾・サロゲート分断の例外、空入力、ASCII大小文字、曖昧入力、Unicode、数字+Latin、seed付き混在・編集後相当列、32/128/256/512/1024 UTF-16を比較。高密度候補ではbeam剪定の同点処理も一致させる。
- .NET 10 ReleaseのCore契約、既存H10/H11評価、Phase1 Trace、Windows Preview無フック、strict-qualityをCIで確認。精度向上とH10入力全長性能改善は主張しない。新規経路採用前に同一runnerペア測定を別途実施する。
- PRレビュー完了までmainへMergeしない。v0.8は後向き・多様性DPを壊す移行を実施しない。

## 4. 実装・検証ログ

- PR [#48](https://github.com/Fugu0141/Meltia/pull/48) にH10モデル固有のGenerator/Transition/Beam/Projectorと内部 `AnalyzeWithSharedPipeline` を追加。元の候補生成ループ・旧前向き検索・旧経路射影は比較オラクルとして維持した。
- 最初のCIで `states` のshadow変数がCS0136/CS0165を起こした。内側宣言を外側の代入へ修正。修正後のコンパイルは成功した。
- 次のCoreテストでは `https://a.co/x?m=a@b.co` がH10本来のURL/mail保護重複エラーになるにもかかわらず成功ケースへ入っていた。H10の `SkipExactDuplicate` とロック衝突ポリシーを変えず、**例外一致ケースへ移して修正**した。これらは実装移植で変えてはならない旧仕様である。
- [Core boundary diagnostics / Actions #39000262563](https://github.com/Fugu0141/Meltia/actions/runs/39000262563) は **SUCCESS / ALL PASS 250**。E3追加6契約：固定103rawの全候補・全Top-Kスコア・確認要一致、ロック/URL/Unicode、seed混在120、UTF-16長32/128/256/512/1024、例外型**と文字列**、連続呼び出しの状態分離が成功。
- 同HEAD `f07cd32fdc5e63a9d1884b98784f640ca6d36933` の9種類のActions（Core/境界品質/H10/H11 Trace/合成ベンチ/入力品質/Windows Preview/strict-quality/PR checks）がすべてsuccess。後日作成されたE4はこのE3ブランチに依存するスタックPRで、E3がMergeされた後にbaseをmainへ切替える。
- 以上は**互換性の証拠**であって、H10精度向上、新Decoderの本番採用や同一runnerでの性能改善、Windows実機打鍵E2E、自然文holdoutの達成を意味しない。レビュー完了までPR #48はMergeしない。
