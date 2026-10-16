# H11 Phase 1 / PR-B — DiagnosticTrace 要件定義・基本設計（2026-10-17）

> **研究用オプトイン診断のみ。** H11の辞書・候補作成条件・探索幅・重み・通常の`Analyze()`・Windows Preview/IME・リリース経路は変更しない。上位仕様：[次期開発準備計画](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 1。Phase 0 [#36](https://github.com/Fugu0141/Meltia/pull/36) / [#37](https://github.com/Fugu0141/Meltia/pull/37) / [#38](https://github.com/Fugu0141/Meltia/pull/38) を前提とする。

## 1. 要件

1. 通常の `H11BoundaryPrototype.Analyze(raw, evidence)` は従来どおりの経路・スコア・例外とし、`AnalyzeWithTrace(raw, evidence)` の明示呼出し時に限り追加情報を収集。**rawやユーザーの私的入力を自動出力しない。** トレース結果を本番IMEへ接続しない。
2. 全採択エッジについてUTF-16範囲・言語・由来・元の `LocalScore` に加えて、base/音韻/文字尤度/語長/辞書/大文字/右継ぎ目/構造/保護ロックなど、**名前付き加点・減点**を記録。浮動小数点加算順序の影響を吸収する `rounding-residual` を分離し、項目和が元スコアと許容誤差内で等しいことを検証する。係数は変更しない。
3. 正式ルートの候補辺・言語遷移・直前言語状態・累積得点と、遷移時の切替ペナルティおよび英語語彙→日本語継ぎ目ボーナスを記録する。すべての候補除外を `UTF-16`・ロック優先順位等の理由別カウントで表す。ビーム刈り込みでは位置・残存言語状態・脱落スコア・閾値を記録。同点時は元の安定ソート・列挙順に従い、**順序を変更しない**。
4. 独立した `CompareToGold(gold)` は**解析後のみ**実行。まず元の候補グラフで正解ラベルの英字区間に整合する経路があるかを判定し、無ければ `missing_candidate` とする。あれば**既存候補だけを使ったGold制約付き無刈込DP**で最良スコアを求め、Top1との差（Top1-Gold）を記録。元の探索の終端生存ノードからもGold整合経路が見つからなければ `path_pruned`、残っているのにTopKに表示されない場合は `topk_omitted`、TopKに存在すれば `ranking` とする。これらは診断分類であり因果推論を断定しない。
5. `yaml` / `new` / `hotelltu` / `Are you sushi?` / `handanwodelegatesuru` を含むIssue診断103件を解析し、通常・trace ON双方が **Phase 0-A1の103ケースの候補順序・Top1/TopK・スコアのfingerprintと完全一致**。gold区間は推論に渡さない。合成ロック/サロゲート/重複入力/Top1=Goldなどの契約・例外試験を加える。
6. 出力JSONは公開Issue由来のラベル・候補範囲・特徴量・環境/入力集合ハッシュだけを持つ。**未公開の自然文holdout・私的打鍵ログを収集・コミット・Artifacts化しない**。JSONにはraw全文をコピーしない。

## 2. 基本設計

- `H11BoundaryPrototype` を `partial` にし、既存 `Analyze` は `AnalyzeCore(..., diagnostics:null)` を呼び出す。計測が明示された時だけ `TraceCollector` を生成する。候補追加・除外、DP prune、終端生存ノードを**横から観測**するだけで、候補・遷移・比較・ソート・減算の順序を変更しない。
- `H11DiagnosticTrace.cs` に診断レコード・collector・Gold制約DPを実装。辺の名前付き寄与値は**同じ式の既存計算値を参照**し、診断以外の演算結果は変更しない。重い記録はトレースONのみ。
- Gold制約DPはルート候補を再生成せず、実際に作成された候補辺から、gold区間内ではEN、区間外ではJA/Neutralだけを通す。状態は直前の有効言語と直前辺が辞書ENかどうかで保ち、H11の実際の言語切替スコアを同じ式で計算。復元に失敗すれば `missing_candidate` を返す。TopKとビーム生存状態は**元の探索結果のみ**参照する。
- ロックやURLで途中候補が排除される際は理由別カウントに記録。全量の個人文字列・除外候補の任意テキストは蓄積しない。正規解析は無変更のまま維持する。
- `tests/Meltia.H11TraceEvaluation/` の独立テスト/診断ランナーはH11Snapshotの既存固定器をリンクし、103件すべてでON/OFFのsnapshotおよび既存凍結ファイルを照合。成功時にのみ指定パスへ診断JSONを出す。新しい公開Issue用JSONはGitHub ActionsのArtifact（期限あり）へ保存する。

## 3. 受け入れと残課題

- **合格**：103件についてON/OFF+凍結fingerprint同一、候補局所項目和一致、確定したルートの各遷移と累積得点一致、候補不在とビーム脱落の非混同、CIの契約試験成功。
- **精度改善ではない**：未解決31件の分類は既知Issue診断の内訳であり一般化した正解率ではない。H11の重みやルートは一切変更しない。
- Goldの最適スコアは**現行候補集合でビームを無視した理論上の最良経路**。現行Top1より高いこともあり得る。スコアの差を「個々の辺ひとつの敗因」と誤解しない。
- Phase 2以降の推論コード共有、Phase 3の性能改善、辞書・ウェイト学習、独立holdoutの評価、Windows実機E2Eは**本PRの対象外**。
