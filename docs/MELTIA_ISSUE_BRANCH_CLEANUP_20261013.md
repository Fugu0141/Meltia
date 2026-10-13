# Meltia Issue / branch housekeeping — 2026-10-13

この文書は2026-10-13時点のGitHubのIssue/PR/branchの棚卸し記録です。今後の開発方針ではなく、再現可能な削除判定・引き継ぎ用です。

## Issueの整理

- [#11](https://github.com/Fugu0141/Meltia/issues/11) — **Closed (completed)**。過去のGitHub Actionsの「0 steps / BlobNotFound」問題。現在のmain `c6884e9` でWindows Preview、Linux、macOS、build、strict quality、input matrixなどのジョブが成功。旧障害の原因そのものは未確定。
- [#19](https://github.com/Fugu0141/Meltia/issues/19) — **Closed (completed)**。PR集約とmain統合が終了。[#21](https://github.com/Fugu0141/Meltia/pull/21)、[#18](https://github.com/Fugu0141/Meltia/pull/18)、[#20](https://github.com/Fugu0141/Meltia/pull/20)、[#24](https://github.com/Fugu0141/Meltia/pull/24)のマージを確認。旧本文のDraft記述は履歴情報。
- 未解決として保持: [#16](https://github.com/Fugu0141/Meltia/issues/16) 正式Windows TSF、[#22](https://github.com/Fugu0141/Meltia/issues/22) notices botの原著作権帰属、[#23](https://github.com/Fugu0141/Meltia/issues/23) リリース管理、[#25](https://github.com/Fugu0141/Meltia/issues/25) 長文性能、[#26](https://github.com/Fugu0141/Meltia/issues/26) URL保護と明示ロック、[#27](https://github.com/Fugu0141/Meltia/issues/27) URL終端、[#28](https://github.com/Fugu0141/Meltia/issues/28) 空挿入のロック不変条件。

## ブランチ調査

GitHubの `main...<branch>` 比較で `ahead_by = 0` は、そのブランチのコミットがmainから到達可能であることを意味します。**未マージのコミットがない**という意味であり、古いPRのCloseだけを理由に削除するものではありません。

2026-10-13の調査時点で、default branchの `main` を含め20本でした。

### 削除候補（15本、すべて main に対して ahead=0）

| ブランチ | mainから見た状態 |
| --- | --- |
| `consolidate/qa-native-core-after-beta1` | ahead 0、behind 15 |
| `docs/meltia-initial-foundation` | ahead 0、behind 291 |
| `feat/meltia-windows-preview-integration-20261012` | ahead 0、behind 85 |
| `feat/v05-meltype-boundary-shadow-bridge-20261011` | ahead 0、behind 223 |
| `feat/v08-phonetic-lattice-decoder-20261012` | ahead 0、behind 206 |
| `feat/v09-contextual-multi-island-20261012` | ahead 0、behind 186 |
| `feat/v10-context-flank-score-phase4-20261012` | ahead 0、behind 113 |
| `feat/v10-diverse-candidate-phase3-20261012` | ahead 0、behind 129 |
| `feat/v10-longrun-boundary-phase2-20261012` | ahead 0、behind 144 |
| `feat/v10-quality-hardening-20261012` | ahead 0、behind 156 |
| `feat/v10-uncertainty-aware-boundary-20261012` | ahead 0、behind 169 |
| `fix/ci-notices-native-tab` | ahead 0、behind 288 |
| `fix/windows-preview-field-test-boundaries-20261013` | ahead 0、behind 1 |
| `release/v1.0.0-beta.1-preview` | ahead 0、behind 44 |
| `test/meltia-input-matrix-20261013` | ahead 0、behind 57 |

これらはPRのマージ済み・統合先反映を確認済みのため削除候補です。**この調査時点ではリモートブランチは削除していません。** 公開済みReleaseのタグは対象外です。

### 保留（4本、mainと履歴が分岐）

| ブランチ | ahead / behind | 対応 |
| --- | --- | --- |
| `feat/meltia-core-v05-shadow` | 3 / 287 | 旧PR #5 はMergedだが独立コミットが残る。現mainとのパッチ同等性を確認してから |
| `qa/strict-repeatable-validation` | 7 / 287 | 旧PR #3。集約先PR #20はMerged。旧差分の一致を確認してから |
| `refactor/composition-responsibilities` | 2 / 287 | 旧PR #2。集約先PR #20はMerged。旧差分の一致を確認してから |
| `refactor/native-acronym-boundary-20261012` | 6 / 286 | 旧PR #17。集約先PR #20はMerged。旧差分の一致を確認してから |

**`ahead > 0` は未取り込み確定を意味しません**（cherry-pickや再実装もあり得る）が、履歴だけで全削除するのは避けます。

## 削除手順（ローカルPowerShell / 認証可能なGit環境）

ここでは削除を実行していないため、実行時には再検証してください。別のPRで再利用中かどうかも確認してください。

```powershell
git fetch origin --prune
$mergedBranches = @(
  'consolidate/qa-native-core-after-beta1',
  'docs/meltia-initial-foundation',
  'feat/meltia-windows-preview-integration-20261012',
  'feat/v05-meltype-boundary-shadow-bridge-20261011',
  'feat/v08-phonetic-lattice-decoder-20261012',
  'feat/v09-contextual-multi-island-20261012',
  'feat/v10-context-flank-score-phase4-20261012',
  'feat/v10-diverse-candidate-phase3-20261012',
  'feat/v10-longrun-boundary-phase2-20261012',
  'feat/v10-quality-hardening-20261012',
  'feat/v10-uncertainty-aware-boundary-20261012',
  'fix/ci-notices-native-tab',
  'fix/windows-preview-field-test-boundaries-20261013',
  'release/v1.0.0-beta.1-preview',
  'test/meltia-input-matrix-20261013'
)

foreach ($branch in $mergedBranches) {
  git merge-base --is-ancestor "origin/$branch" "origin/main"
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "Skip non-ancestor or absent branch: $branch"
    continue
  }
  Write-Host "Deletion candidate: $branch"
  # 実際に削除する際は下記コメントを解除する:
  # git push origin --delete $branch
}
```

`origin/main`、GitHub Releaseタグ、進行中PRのheadは削除しない。実行後に `git fetch origin --prune` でリモートを再確認する。履歴が分岐した4本は別途差分・パッチの同等性が確認できるまで削除保留。

## 運用ルール

1. PRマージ時にはGitHub側の「Delete branch」を使う（後続PRのbase/headとして利用中でない場合）。
2. クローズのみのPRは、代替PRへの取り込みを確認した後で削除可否を判定。
3. 判定は `git merge-base --is-ancestor` またはGitHub compareで確かめる。単なるClose、同一タイトル、似たファイル名では判断しない。
4. リリースはタグで保持し、作業用releaseブランチを永久に残す必要はない。ただし今後のホットフィックス計画がある場合は別途判断。
