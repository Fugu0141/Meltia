# H11 Phase 4 / PR-E1 — 共通前向きDecoderの設計・オフライン同値比較（2026-10-17）

> 起点：main `2878c392b80c0fc6fd00324dd15fc2bd29539f78`（Phase 3 D2 PR #45統合後）。上位要件：[`MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md`](MELTIA_H11_NEXT_DEVELOPMENT_READINESS_20261016.md) Phase 4 / PR-E。**これはPhase 4全体の完了ではなく、E1（共通前向き探索の実験的並列実装）です。**

## 1. 要件・非対象

**目的**：次期H12でH11の探索ループを複製しないため、候補集合・遷移計算・ビーム保持の境界を明確化した内部共通前向きDecoderを導入する。出力互換性を最優先し、現在のH11 `Analyze()` は**旧実装のまま**比較オラクルとして残す。

- 旧H11の候補順・重複・Language状態順（JA→EN→Neutral）、`double`計算順、beam発火件数（33到達時16件へ剪定）、`List.Sort`の同点時非安定性、Prev参照、TraceのDrop順、finalistsの`OrderByDescending`・`Take(48)`・英語区間signature dedup・上位8・不確実判定を**完全不変**に保つ。
- 依存境界：`IForwardCandidateProvider<TEdge>`が候補を位置順に供給し、`IForwardTransition<TNode,TEdge>`が遷移状態/スコア/ノード生成を担い、`IForwardBeamRetention<TNode>`が同点を含め既存順序で剪定し、`ForwardLatticeDecoder`が位置×状態の走査とノードの格納を行う。**ルート投影は現H11に残す**（後続E2でインターフェースと契約を設計する）。
- E1は**H11だけ**が内部`AnalyzeWithSharedDecoder` / `AnalyzeWithSharedDecoderTrace`で明示的に実験利用できる。通常`Analyze`/公開`AnalyzeWithTrace`/Windows Preview/TSFには接続しない。自動確定禁止を維持。
- H10前向き幅8・H11前向き幅16と、v0.8後ろ向き・区間数多様化DPは別アルゴリズム。**今回H10とv0.8を移植しない。**
- 候補生成・辞書・保護ロック・UTF-16安全規則・特徴量・重み・遷移コスト・Top-K・性能フラグ・Preview設定・Release/配布を変更しない。

## 2. 基本設計と互換性契約

1. **候補Graph（現H11生成）**：`edges[i]`を元の挿入順のまま`IReadOnlyList<TEdge>`として公開するだけ。辞書・音韻生成を変更・再生成しない。型アダプターは解析呼び出し内限定、共有キャッシュなし。
2. **Transition（現H11算術）**：`previous.Last` / `candidate.Lang`による言語切替 `-1.6`、直前英語辞書語の後に2文字以上の日本語音韻へ続くときの `+0.9`、Neutral時の直前状態維持を同じ順序で評価。`new Node(previous.Score + candidate.Score + transition, ...)`までの浮動小数点順序・バックポインタは不変。
3. **Beam（現H11剪定）**：`destination.Count > 32`時に`Sort`→`diagnostics?.Drop`→`RemoveRange`。同点の任意な安定化・候補削除・幅変更はしない。
4. **Forward decoder**：入力長×状態数の`List<TNode>[,]`を毎回生成、始点はNeutral/score0、位置→言語→既存ノード→候補（同じ列挙順）を走査。`edge.End > position`と`edge.End <= raw.Length`を満たさない不正な候補は共通エンジンで拒否（旧H11生成ではそもそも発生しない）。`dp`を呼び出し元へ返し、候補列挙と経路復元はH11側に残す。
5. **比較ゲート**：旧H11と新Decoderを**同一入力・同一Evidence**で実行し、raw、Edgesのスパン/言語/origin/scoreの完全順序、Routesの全8件のスパンとscore（`Double.Equals`）、NeedsConfirmation、AutoCommitAllowedを比較。Trace ONではBeamDrop順序/スコア・route steps・拒否理由を照合。最大長1024・途中編集・ASCII大文字・Url/Email・曖昧日本語・ロック・Unicode/surrogate・同点/最大幅・例外も負例とする。
6. **凍結値**：Phase0の103件は通常H11の既存fingerprintオラクルで検証。E1では同じ103入力に対して旧と共通Decoderを比較するテストも追加する。**英語判定Top1だけでは合格にしない**。

## 3. テスト・採否・段階移行

- E1の新Decoderは**実験オプトインのみ**。全103件の参照モデル同値（raw/候補/全Top-K/スコア/ロック）・Trace・乱数・編集・例外が成立するまで通常`Analyze`を置き換えない。
- .NET10 Release Core契約、H11凍結103、Phase1 Trace、H10比較、H11 synthetic benchmark、Windows Preview無フック、strict-qualityのCIを必須とする。
- 既存入力に対する性能の大幅な退行があれば、共通化の費用をプロファイルで切り分け、E1の新経路を実際の推論へ切替えない。
- E2で候補生成器/経路復元のインターフェース化、H11移行の評価を別PRで進める。E3以降のH10アダプターやv0.8は別の要件・回帰テストを要する。Phase4完了として扱わない。
- 実機Windowsの入力E2E・独立自然文holdout・Meltype対実測比較は未実施であり、今回のCI合格で代替しない。

## 4. 実装・検証ログ

- **E1実装**：`src/Meltia.Core/Boundary/ForwardLatticeDecoder.cs` に、入力位置×状態×経路×候補を回る内部ジェネリック探索器。候補Provider、遷移Scorer、ビームPolicyの3契約を定義。`H11ForwardLatticeAdapters.cs` にH11専用の互換アダプターを配置。既存 `H11BoundaryPrototype.AnalyzeCore`の探索ループは変更なしの`else`枝へ移し、`useForwardDecoder=false`既定。`AnalyzeWithSharedDecoder`と`AnalyzeWithSharedDecoderTrace`だけが明示的に新経路へ入る。どちらも`internal`なので製品APIを広げない。
- **E1テスト**：`tests/Meltia.Core.Tests/Phase4E1ForwardDecoderTests.cs`。独立toyグラフで状態遷移/beam保持/不正な後退・範囲外エッジを検査。旧H11 vs 共通H11について、47+56=**103 raw**の**候補の全順序・スコア/全Top-K・Trace（Drop・Reject・スコア項・ルート）を完全比較**。明示EN/JA・Protected、メール/URL、絵文字、数値英語、名前、空文字を検証。さらにseed固定125例の混在入力、32/128/256/512/1024 UTF-16、セッション外での後続再実行、最大長/ロック不正・surrogate分断時の例外型の一致を検証。
- **初回CIの不合格・是正記録**：最初のテストでTraceの公開プロパティ名を誤って`OmittedDropEvents`と記述しCS1061で停止した。実際の公開名`OmittedBeamDropEvents`に修正して再実行。**アルゴリズム誤差ではないがテスト作成上の不具合**として記録する。
- **Core CI**：[Actions #38997919213](https://github.com/Fugu0141/Meltia/actions/runs/38997919213) で **ALL PASS 238**（10件のE1追加契約）。同値ゲートの合格はH11の**新Decoderとの互換性**の証拠であり、旧モデル自体の認識精度改善や独立した言語ラベルの新規評価ではない。
- **既存ゲート**：同HEADのH11 Phase1 DiagnosticTrace、H10比較、合成ベンチ、入力品質などのActions成功も確認する。CI status URLは[PR #46](https://github.com/Fugu0141/Meltia/pull/46/checks)を参照。
- **明確な未完了事項（E2以降）**：旧H11のルート復元/Top-K射影インターフェースの抽出、候補生成器の完全分離、共通Decoderの**本番経路採用・性能の同一runner比較**、H10/v0.8の仕様差別個のアダプター、Windows実機E2E。今回のPRではすべて保留。通常`Analyze`を共通エンジンへ自動切替しない。

