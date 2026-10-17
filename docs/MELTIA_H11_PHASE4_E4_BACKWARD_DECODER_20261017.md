# H11 Phase 4 / PR-E4 — v0.8系後向き探索の分離・互換実験（2026-10-17）

> E3 `feat/h11-phase4-e3-h10-forward-adapter` に積む**依存PR**。E3のレビュー・main取り込み後に本PRをmain向けへ変更して再度CIを確認する。既存v0.8経路・Windows Preview・H11既定入力は変更しない。

## 要件・適用範囲

Phase4-E1/E2は前向き3状態×有限ビームのH11/H10用であり、v0.8の `MeltiaPhoneticLatticeDecoder.Decode` とは計算方向も保持する仮説が違う。

- v0.8はUTF-16入力の**終端から始端へ**DPし、候補外のJA読み、未知ASCIIペナルティ、明示JAの免除と強制EN/URLのブロック、複数英語候補が接尾辞に与えるスコアを扱う。
- `maxRoutes` 件の単純なスコア順保持では**区間数ごとの代表経路**が消える。現コードの `OrderByDescending(Score)` → 四桁ゼロ埋めstart-endのordinal tie → 区間列でDistinctBy → 0〜4個のEnglishSpanCount層代表 → 残り高得点補完 → 最終再ソートの順序を変更しない。
- v0.9 `contextualIslandScoring`、v1.0 `experimentalJapaneseFlankScoring` はv0.8後向き展開へ組み込まれたオプションであり、スコアと経路順序を維持。採点用goldを渡さない。
- 自動確定は元の `MeltiaBoundaryEngine` が禁止したまま。研究用の `MeltiaPhoneticBackwardComparison.Decode` は`internal`で比較テストだけが呼ぶ。製品既定ルートは絶対に切り替えない。

## 基本設計

1. `IBackwardOfferProvider<TRoute>`: `OffersAt(position, suffixes)` で読み・ブロック・候補・文脈ボーナス・スコア加算の**モデル固有**操作を実施。
2. `IBackwardRouteRetention<TRoute>`: 各位置で重複・区間数層別・Top-Kの**モデル固有**順序を実施。v0.8の多様性を共通側の一律ビームに置き換えない。
3. `BackwardLatticeDecoder`: `raw.Length`を終端初期値にし、`i=length-1...0`と方向のみ共有。方向以外の英語/日本語の意味は持たない。
4. v0.8の現実装 `MeltiaPhoneticLatticeDecoder.Decode` は完全な旧比較オラクルとして保存。候補準備と二段階の選抜を再構成した**別の内部オフライン実験経路**を比較する（重複ロジックは採用判定まで意図的に保持）。同値が確認できた後に将来の別PRで一本化判断する。

## 受け入れ・リスク

- 同一候補・同一raw・同一ロック・同じflagsで**経路数・英語スパン全順序・`Double.Equals`のscore完全一致**。最終Top1だけでは不十分。
- UTF-16絵文字、ASCII romaji、URL/mail由来の保護範囲、複数英語区間、候補重複/同じspan、0〜8区間、タイブレーク、高得点・負点、maxRoutes境界、空入力、明示JA/EN、v0.9/v1.0オプションを含める。例外型とメッセージも可能な範囲で比較。
- 旧Coreのデフォルト `BuildRoutes` も**後ろ向き**であり、E1前向きDecoderへ移植できない。今回はv0.8研究経路のみを対象とし、旧Core通常経路はオラクルのまま。後続の採否レビューで共通の後ろ向きDPへの限定的な移行を検討する。
- .NET10 Release Core・H11固定103・H10・Trace・Windows Preview無フック・strict-qualityを通す。旧v0.8を置換せず、実機Windows E2E・自然文holdout・同一runner性能比較は別ゲート。
- 本ブランチはE3に依存する積み上げPRであり、E3が未Mergeの段階で本PRをmainへMergeしない。

## 検証記録

- PR [#49](https://github.com/Fugu0141/Meltia/pull/49) に `BackwardLatticeDecoder` と `MeltiaPhoneticBackwardComparison` を追加。旧 `MeltiaPhoneticLatticeDecoder.Decode` は**一切編集せず**、2経路を明示的に比較した。新比較経路の候補展開ループは旧v0.8の該当2,914文字の実装と文字単位で完全一致することを確認した。
- **初回のコンパイル失敗**：新クラスからprivate定数 `MaxJapaneseEdgeLength` / `UnreadableAsciiCost` / `ReadableJapaneseCharReward` を参照できずCS0103。値は旧v0.8と同じ `8 / 3.5 / 0.25` を比較器内に明示して再実行。旧本体は変更なし。
- [Core boundary diagnostics / Actions #39000607801](https://github.com/Fugu0141/Meltia/actions/runs/39000607801) **SUCCESS / ALL PASS 256**。E4追加6契約：v0.8/v0.9/contextual/flank全Top-Kの一致、110 seed混在、103凍結raw、UTF-16長32/128/256/512/1024、区間数多様性・Top-K上限・forced、重複flank例外型とメッセージがPASS。
- 最新の**コードHEAD `382d835ad2ee4650f901f4ec9a52ee9a00b3d2a6` の8種類のActions（Core・H10比較・H11 Phase1 Trace・合成ベンチ・境界品質・Windows Preview・strict-quality・PR checks）はすべてsuccess**。これは共通化した実験経路の互換性に限定され、自然文精度やWindows打鍵E2Eを示さない。
- 引き続きE3 [#48](https://github.com/Fugu0141/Meltia/pull/48) の上に積まれた依存PR。さらにE5 [#50](https://github.com/Fugu0141/Meltia/pull/50) では従来Coreの単純Top-K後向きDPを別のRetentionで接続する。いずれもレビューを経ずにmainへMergeしない。
