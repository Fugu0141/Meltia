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

### D2 実装の初回CIと再測定（2026-10-17）

- PR [#45](https://github.com/Fugu0141/Meltia/pull/45)、初回実装HEAD `a408664764ff8012598603e5dbff7636b263faf1`。マイクロベンチの試験時間が短かったため、改良HEAD `992003238dc42c6a379d6847eaa9c2af97cf672c`で**各試行10,240ビーム×6試行・計測順の交替・中央値**へ変更した。
- [Core契約の再計測 #38994897931](https://github.com/Fugu0141/Meltia/actions/runs/38994897931) は **0 Error / ALL PASS 228**。2,800件乱数＋7種の境界負例（同点・NaN・±0・無限大など）の保持16参照と順序が凍結した`List.Sort`方式と一致。33件未満の非剪定もPASS。新方式を断念した場合、変更前リストを保持して旧ソートに戻る。
- [H10比較＋H11固定103件 #38994897588](https://github.com/Fugu0141/Meltia/actions/runs/38994897588) はSUCCESS。元の47件＋追加56件のH11完全fingerprintが一致した。これは既知Issue診断の一致であり、新規自然文精度の実測ではない。
- [Phase1 DiagnosticTrace #38994897658](https://github.com/Fugu0141/Meltia/actions/runs/38994897658) と[合成ベンチsmoke #38994897687](https://github.com/Fugu0141/Meltia/actions/runs/38994897687) はSUCCESS。Traceは常に旧ソート経路を通り、脱落者順序の互換を維持する。

単体測定：GitHub Actions Linux runner / .NET 10 Release / 同一ジョブ / 合成33要素固定ビーム、元のNode参照を使用。ベンチは毎試行リストを再生成するため、両方式の`List<Node>`再生成コストも含む。各モードの160fixture×64反復=10,240 beam/pruning操作を、各方式6回測定（事前各1回ウォームアップ、実行順を交替）。順序依存FNV型ハッシュは6試行すべて同値。

| Score分布 | 旧方式 median | D2 median | 旧割当/試行 | D2割当/試行 |
| --- | ---: | ---: | ---: | ---: |
| 全てユニーク（高速経路が成立） | 32.432 ms | 18.989 ms | 3,276,800 B | 3,276,800 B |
| 同点多数（旧方式へ戻る経路） | 20.627 ms | 17.519 ms | 3,276,800 B | 3,276,800 B |

**測定解釈の注意**：同点多数のケースは保持上位に同点があるため、D2の選択処理の後で旧ソートを必ず実行する。理論的に計算量が増えるこの分布で見かけ上速い値が出たのは短い単一ラン・JIT/CPU/スケジューラ等の交絡があることを示し、**同点ケースに速度改善があったとは認めない**。ユニーク分布の速度差も合成ソート局所測定であり、現実のH11全体・Windows入力が改善した証拠にはならない。両方式の割当同数は**ベンチで同じList再生成を含む**ためで、H11全体のGC削減を意味しない。

### 未実施・別途必要な測定

- Phase0の32/128/256/512/1024文字＋編集の**main対PR HEADの同一CPU/ランタイム条件でのペア測定**は未実施。同一の未特定クラウドrunner同士で異なるActions実行の時間を直接比較しない。
- 本PRには候補生成削減やビーム幅の変更は含まない。多重候補の意味的統合には別要件・専用のTop-K全件同値オラクルが必要。
- 最終採否には全CI・独立レビューが必要。研究用結果をWindows Previewの実キー入力E2Eと混同しない。
