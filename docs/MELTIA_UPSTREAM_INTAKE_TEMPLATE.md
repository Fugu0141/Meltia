# Meltype上流変更のMeltia受け入れ判定シート（複製用）

> `docs/MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md` に従う。**このファイルは雛形であり、空欄のままでは受け入れ許可ではない。** コピー先に対象ごとの日付・PR/commitを記録する。

## A. 同定と差分の出典

| 項目 | 記録 |
| --- | --- |
| 作業ID / Meltia Issue | TODO |
| 上流 Meltype PR/Issue | URL / ID / 状態（open/merged/closed） |
| 上流固定commit/tag | 完全なSHA |
| 上流「前回追跡対象」と比較した基点 | 完全なSHA |
| Meltia比較起点main | 完全なSHA |
| B→U（上流差分） | 変更ファイル一覧と比較URL |
| B→M（Meltia独自差分） | 競合ファイル・意味差 |
| 作者・ライセンス・依存 | SPDX/改変表記/辞書出典/第三者条件 |
| 変更の種類 | 修正 / 機能追加 / 辞書（その他の変更は理由と扱い） |
| 所有領域・リスク | Core / Composition / Host / native / config / OS / workflow。通常/高リスク/禁止 |
| 採用判定 | covered / direct / adapt / redesign / deferred / rejected |
| 判定理由と代替案 | 根拠・既存Meltiaへの影響・却下理由 |

## B. 要件定義（移植の前に確定）

- 背景・解決したい具体的な問題：
- Meltypeの現行仕様（比較する再現入力と期待結果）：
- Meltiaの現行仕様（同じ条件の旧結果）：
- 受け入れる上流の機能／修正：
- 受け入れない機能と理由：
- **変えてはいけない挙動**（UTF-16、原文、確定、入力順、ロック、設定、辞書、学習、更新など）：
- **意図して変える挙動**（変える場合は別PR）：
- 戻す手順、オプトアウト、データ保全：
- 対象OS・必要な実機試験：
- テストの受け入れ条件／性能の許容条件（案件ごとに定義）：

## C. 基本設計（実装前）

- 関係するファイルと呼び出し関係：
- 責務を置く先（Meltype互換層／Composition Adapter／Meltia Core／Windows Preview等）：
- データ契約・境界（raw座標、区間表現、例外、所有/寿命、スレッド/イベント順序）：
- 副作用（Commit / Replay / Delete / Save / Network / COM / Install）：
- 上流の設計上の問題と解決案（**互換リファクタリングと意味変更は分離**）：
- 上流変更の依存PR/commitと未採用の前提：
- 既存Meltypeとの関係（共存・更新元・データ分離）：

## D. 検証・レビュー記録

| ゲート | 参照・実測・結果 |
| --- | --- |
| 凍結Meltia既定値と同値／意図差の説明 | TODO |
| 上流修正の再現テスト・上流期待値確認 | TODO |
| Core/Composition/H10/H11/Top-K/ロック（対象のみ） | TODO |
| 例外、emoji/UTF-16、英日混在、長文編集 | TODO |
| 性能（同一runner A→B→B→A、p50/p95/p99、割当等） | TODO |
| Windows無フックスモーク／実キー入力（必要に応じ） | TODO |
| 保存先・ネイティブ・配布／ライセンス・通知 | TODO |
| GitHub Actions・コードレビュー・未解決指摘 | TODO |
| PR URL / 最終HEAD / merge SHA | TODO |
| 既知の問題と未達ゲート | TODO |

**完了判定**：レビュー承認と必須ゲートが揃うまではMergeしない。未採用・保留の上流コミットは次回の比較から落とさない。想定外の退行時は当該PRの差し戻しを検討し、原文・ユーザーデータ保全を優先する。
