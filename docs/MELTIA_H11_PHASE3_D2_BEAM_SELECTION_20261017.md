# H11 Phase 3 / PR-D2 — ビーム保持最適化の契約・比較実験（2026-10-17）

> 対象は研究用H11 Decoderのみ。起点 main `bb8ec3b344dc8b111626b2885daa3c2e68c1d9ef`（D1b PR #44 統合後）。上位の要件：[H11次期開発準備](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 3 / D2。

## 1. 課題と非対象

H11の各UTF-16終端位置×言語状態には`List<Node>`がある。候補のたびに経路ノードを追加し、`Count > Width * 2`（Width=16、33件目）で **Score降順の`List.Sort`→後半17件除去** を実施する。32件までは未ソートで保持される。最後のTop-Kは全言語の終了状態を合わせて別途安定順序の`OrderByDescending`で選ばれる。

- **目的**：33件の全ソートの代替として、選択＋上位16件だけの小配列順序づけを独立比較し、安全であれば通常推論のみに採用する。ビーム候補の再生成や学習重みの変更は行わない。
- **互換契約**：33件到達時刻、16件の残留参照・順序、Scoreの`double.CompareTo`順、同点・NaN・正負0・マイナス値・同一経路多重登録・バックポインタ、Top-K署名、原文/保護区間、例外は旧ソートと完全一致。
- **重要な留保**：旧`List.Sort`は同点時に安定とは限らない。保持上位内または保持境界を跨ぐ同点がある場合は、元のリスト順序を一切変えず**旧ソートへフォールバック**する。同点が脱落側だけにある場合は保持16件の参照と順序は不変。DiagnosticTraceがONのときは脱落者の順序・全件を維持するため必ず旧方式。
- **非対象**：H10 / 旧Core、H11候補の重複排除・統合、辞書/スコア/候補生成/ビーム幅の変更、Windows Preview/TSF、本番入力・ログ。経路が同値でも多重候補を消すとTop-K多様性が変わるため、D2では候補削除を行わない。

## 2. 基本設計と負例

`H11BeamSelection.TryPruneUnique(list, width)`は長さ `2*width+1`（H11では33）にだけ動く。短い`stackalloc int[]`の**元の添字**に対して上位16件をQuickselectし、選択部分だけ挿入ソートする。元リストは順位と同点を確定するまで触れない。保持候補同士の同点または保持側最下位と脱落候補の同点があれば`false`を返し、旧`List.Sort`へ戻す。完全に一意な保持上位16件だけを、参照/バックポインタをコピーせずその場のswapで元リスト先頭へ並べて末尾を除去する。追跡有効時はそもそもこの関数を呼ばない。

固定seedでランダムなScore順列・逆順・極値・同点・NaN・±0・17〜33件の蓄積で旧式の結果と**参照順序**比較する。候補重複が許されることもテストする。Phase 0凍結103件の候補/全経路/スコア/fingerprintとPhase 1 Traceの差分ゼロを必須とする。

## 3. 測定と採否

同一プロセス・.NET 10 Release・ウォームアップ後に33件固定の`List<Node>`に対して旧Sort/RemoveRangeと新方式（同点時フォールバック）を比較する。処理時間・スレッド割り当て量・結果フィンガープリントを報告する。さらにPhase 0の短文/長文/編集ベンチ（32/128/256/512/1024 UTF-16）を **同じCPU/.NET/辞書/反復条件** でmainとPR HEAD比較する。単一CIマシン・少数反復で改善率や実機打鍵性能を断定しない。

検証コマンド：
```sh
dotnet run --project tests/Meltia.Core.Tests/Meltia.Core.Tests.csproj -c Release
dotnet run --project tests/Meltia.H10Evaluation/Meltia.H10Evaluation.csproj -c Release -- --verify-baseline tests/Meltia.H10Evaluation/h11_issue_baseline_v1.jsonl
dotnet run --project tests/Meltia.H11Benchmark/Meltia.H11Benchmark.csproj -c Release -- --smoke --output artifacts/phase3-d2-smoke.json
```

**受け入れ**：Core新規同値試験、H11 103件指紋、Phase1 trace、H10、Windows Preview無フックとstrict-qualityが通ること。測定未実施・明確な速度退行・同点の不一致があれば切替を採用しない。レビュー完了までmainへMergeしない。

## 4. 実測結果と制約

未取得。PRのActions・比較ベンチの条件/数値を実測後に追記する。
