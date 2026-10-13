# Issue #23 — 過去タグの Linux Release 公開経路を隔離する

**対象**: [Issue #23](https://github.com/Fugu0141/Meltia/issues/23)  
**基準 main**: `98159a33a2dcbf5d856ffdb39cbaa611fc416156`（2026-10-14 調査時）  
**旧タグ**: `v1.0.0-beta.1` = `7894860d9a8b0960bf6a25d27fec5c0712deb059`（移動・再作成禁止）  
**既存 Release**: [Windows Preview](https://github.com/Fugu0141/Meltia/releases/tag/v1.0.0-beta.1)

## 再現（破壊操作なし）

過去コミットの `.github/workflows/linux.yml` は `workflow_dispatch` と `push.tags: ['v*']` を持ち、Linux ZIP 作成後にジョブ単位で `contents: write` を与えて `softprops/action-gh-release` を呼び出す。タグを指定して実行（または過去実行を再実行）すると、既存の Windows Preview Release に `Meltype-1.0.0-beta.1-linux.zip` が追加され得る。

```bash
git show 7894860d9a8b0960bf6a25d27fec5c0712deb059:.github/workflows/linux.yml
git rev-parse refs/tags/v1.0.0-beta.1
python3 tools/test_legacy_linux_release_guard.py
```

**過去ワークフローを実際に Dispatch / Re-run して再現しないこと。** 危険な公開副作用があるため、コードを読み取る非破壊テストで再現する。

2026-10-14 の公開 GitHub API 観測: Release ID `410669509`、`immutable: false`、Assets は以下の **2点のみ**。

| Asset | SHA-256 digest |
| --- | --- |
| `meltia-windows-preview-win-x64-framework-dependent.zip` | `e53039c79bcd04103dd05dd52fa766b4b788af075dca0c30c1b440df1c794ef7` |
| `meltia-windows-preview-win-x64-framework-dependent.zip.sha256` | `f660ab08da8bc035d36425f2911ed557526948a2928b450d7e312fd3414fb208` |

## 設計上の判断 / 最小修正

1. 現行の **Artifact 専用 Linux CI** を `.github/workflows/linux-artifacts.yml` へ移す。ワークフロー名を `linux-artifact-ci` にし、旧 `linux.yml` の GitHub Actions ワークフロー ID と分離する。テスト工程、トリガー（Ready PR/main/手動）、Artifact 生成は維持する。新ワークフローの `paths` 自己参照だけ新ファイル名へ移す。
2. `.github/workflows/linux.yml` は **最新 main からのみ削除**する。既存のタグ・Release・ダウンロード URL・アセットは変更しない。過去 ref の古い YAML 自体が修復されたと誤認しない。
3. 軽量な読み取り専用回帰 CI (`legacy-linux-release-guard`) を追加。固定 SHA の古い YAML に公開経路が実在すること（再現）、新 CI が `contents: read` / `actions/upload-artifact` であること、旧ファイル名を再導入していないこと、タグ SHA が変わっていないことを検査する。
4. **必須の管理者操作（下記）を実行しない限り Issue は完了扱いにしない。** PR とコード変更だけでは古い Actions workflow ID や過去実行の権限を取り消せない。

## 管理者操作（必須、既存Releaseを変更しない）

**PR を main にマージする前に** リポジトリ管理者が旧ワークフローだけを無効化する。無効化中は既存の Linux テストが一時停止するが、マージ後は *別ID* の `linux-artifact-ci` が新たに動く。

1. GitHub の `Actions` → 旧 `linux` → 右上 `…` → **Disable workflow**。他のワークフローを無効化しない。
2. または GitHub CLI 認証済み端末で `gh workflow disable linux.yml -R Fugu0141/Meltia`。
3. `gh workflow list --all -R Fugu0141/Meltia` と `gh api repos/Fugu0141/Meltia/actions/workflows` で旧 `.github/workflows/linux.yml` の workflow ID と `disabled_manually` を確認し、ID・日時を Issue に記録する。
4. マージ後も旧IDを有効化しない。新たな Linux テストは `linux-artifacts.yml` だけを実行する。
5. 過去実行の **Re-run** は元の SHA/ref と権限を用いる。Disable が過去実行の再実行まで防ぐと確かめられない場合は、安全なGitHubサポート等での確認または追加の制限が必要。**危険な旧実行を試しに再実行しない。**

GitHub公式: [ワークフロー無効化](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/disable-and-enable-workflows)、[再実行の権限・ref](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)。

### 補助防御と注意点

- GitHub `Settings` → `Actions` → `General` の許可アクション制限で `softprops/action-gh-release@*` を除外する方法は、**旧 Linux だけでなく現行の `build.yml` / `mac.yml` にも影響**する。影響する他ワークフローを精査し、別Issueと調整せずにリポジトリ全体へ適用しない。アクションの禁止は、直接APIアップロードまでは防げない。
- Repository の **immutable releases** を今 ON にしても、**既存 Release は自動的に immutable にならない**。既存 Release の再公開は本Issueの「変更しない」条件に反するため行わない。GitHub公式: [2025-10-28 発表](https://github.blog/changelog/2025-10-28-immutable-releases-are-now-generally-available/)。
- **「Actions のデフォルト `GITHUB_TOKEN` 権限を read にする」だけでは不足**。過去YAMLにはジョブ単位の `contents: write` が明記されている。
- 公開アセット全体の安全性には、発行可能な他のワークフローや特権トークンの運用を別途確認する必要がある。Issue #23は旧Linux経路に限定する。

## 検証方法と完了判定

```bash
# full history / tags を取得したリポジトリで実行
python3 tools/test_legacy_linux_release_guard.py

# GitHub API への読み取り専用チェック: タグ・既存Assets・旧workflow ID
# old workflow の state が disabled_manually でなければ失敗させる
python3 tools/test_legacy_linux_release_guard.py --live

# 管理者が旧ワークフローを無効化した後に確認
gh workflow list --all -R Fugu0141/Meltia
```

- [x] 既存タグ・Release（SHA/ID/2資産名とdigest）の調査と記録
- [x] 古い tag YAML のRelease書き込み経路の静的再現
- [x] コード変更と回帰テストの追加（GitHub CI結果は別途確認）
- [ ] 旧ワークフロー ID に対する **管理者 Disable の実施と実測**
- [x] PR の回帰CI / Linux CI の結果を記録（レビュー時点のhead `0c95a9c` で5件の回帰テスト、Linux CI、strict quality、build、PR checksが成功。レビュー指摘によるヘッダー修正後の回帰テストも成功。最終headの他ジョブはActionsで再確認）
- [ ] 過去 run の Re-run への運用上の制限・確認結果を記録
- [ ] `--live` による公開Release不変・旧workflow無効化の確認

## 2026-10-14 レビュー対応と検証結果

- [PR #30](https://github.com/Fugu0141/Meltia/pull/30) で指摘された新規Pythonテストのライセンス・著作権ヘッダーを追加した。先頭は shebang、`# SPDX-License-Identifier: GPL-3.0-or-later`、`# Copyright (C) 2026 Fugu0141` の順にする。
- レビュー対象head `0c95a9c` の `legacy-linux-release-guard`（5テスト）、`linux-artifact-ci`、`meltia-strict-quality`（Linux/Windows）、`build`、`pr-checks` は GitHub Actions で成功済み。
- ヘッダー修正コミット `a6c18f6` でも `legacy-linux-release-guard` は成功。変更対象はテストファイルのヘッダー3行のみで、ランタイムアルゴリズムやZIP生成処理を変えない。
- 新規コード/テストの作成と修正では生成AI（ChatGPT）を補助的に使用。GitHubのコードレビューとActionsで確認した事項のみ「検証済み」と記録する。
- GitHubの公式文書では **Disable は新しいトリガーを停止するための操作** とされ、Re-run は元の SHA/ref/actor 権限を使用する。Disable によって古い実行が再実行できなくなるとは保証されていないため、`disabled_manually` だけでIssue受け入れ条件を満たしたと結論しない。確認不能な場合はGitHub Supportに運用上の保証を問い合わせるか、既存のCI/Releaseへの影響を評価した上で別の管理ポリシーを設計する。
- 2026-10-14現在、旧workflowの管理者Disable実施と `--live` 監査結果は **未確認**。リリース書込み試験のために過去タグのワークフローを実行しない。

参考: [GitHub workflowのDisable](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/disable-and-enable-workflows)、[過去runのRe-runと権限](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/re-run-workflows-and-jobs)。

## 2026-10-14 再レビュー（HEAD `0e9cf71`）への対応

- レビューではSPDX/Copyrightは解消済み、最新HEADの `legacy-linux-release-guard` 5件と strict quality（Windows / Ubuntu）は成功と確認された。一方、`linux-artifact-ci` / `build` / `pr-checks` はDraftや変更対象条件によりskipしており、古いコミットの成功を最新HEADの成功として扱わない。
- 最新 `main` `32ac40618b5298d1362a1a33b22ff602729b69ec` を、本PR専用ブランチに **二親のmerge commit** `8d9b6cdede82a40817f6aa82177dd05d77c42a9c` で取り込んだ。main側の唯一の追加コミットは Issue #28 のセッション空挿入修正であり、今回のIssue #23の対象ファイルとの変更重複はない。
- GitHub compare（取り込み直後）: `main...fix/issue-23-legacy-linux-release-guard` は **ahead 6 / behind 0**、差分はIssue #23の7ファイルのみ。Issue #28のソース・テスト・Markdownには一切手を加えない。
- `8d9b6cd` で `legacy-linux-release-guard` 成功、`meltia-strict-quality` は確認時実行中、`linux-artifact-ci` / `build` / `pr-checks` はDraft/paths条件によりskipと記録する。すべてを成功とみなしてマージしない。
- 固定タグ `v1.0.0-beta.1` は従来どおり `7894860d9a8b0960bf6a25d27fec5c0712deb059`、既存ReleaseはID `410669509` で2点のWindows ZIP/sha256資産のdigestが変わっていない（GitHub APIをread-onlyで再確認）。
- **管理者セキュリティゲートは未達**。GitHubワークフロー管理APIの変更を行える接続を使っていないため、旧IDの `disabled_manually` は独立確認していない。過去実行のRe-runについても保証は得られていない。危険な旧runを試行しない。管理者が旧IDの状態・日時・根拠を記録し、`--live` 成功を確認するまでIssue #23はOpen/PR #30はDraftのままとする。

**管理設定を実際に確認するまでは、過去refによるRelease書き換えが防止済みとは宣言しない。**

## 2026-10-14 追加修正 — 読み取り専用監査のfail-closed強化

- [PR #30](https://github.com/Fugu0141/Meltia/pull/30) の `tools/test_legacy_linux_release_guard.py` は `--live` の安全性判定を `assert` から明示的な `RuntimeError` に変更した。Python の `-O` 最適化オプションで検査が無効化されない。
- 読み取り専用監査は既存タグ SHA、Release ID `410669509`、公開状態、2アセットの名前・Digestに加え、アセット ID（`633960588` / `633960585`）の変化も拒否する。追加の Linux ZIP やアセット差し替えがあれば失敗する。
- 正常なAPI応答・旧workflow `active` / 見つからない状態・タグ移動・Release/Asset差し替え・アセット追加/下書き化を模擬した **5 件の追加回帰テスト**を導入（計10件）。これらはネットワークアクセスや過去ワークフロー実行を伴わない。
- GitHub公開APIで読み取れる範囲のタグ・Release/資産は変更されていない。一方、旧workflow IDの管理画面状態と歴史的runのRe-run遮断は未確認であり、`--live` の合格は**実施済みと扱わない**。
- 旧タグの歴史的ファイル名が文書で誤って `linux-artifacts.yml` と書き換わっていたため、`docs/MELTIA_OS_E2E_VERIFICATION.md` を `linux.yml` に訂正した。新CIのファイル名は `linux-artifacts.yml` のまま。
- 最新 `main` が調査時点で旧PRの基点から28コミット進んでいる。並列作業へ影響しないよう、本対応では `main` をブランチへ機械的に統合していない。最終レビュー・マージ前に差分とCIを再確認する。

**未完了**: 管理者による旧workflow IDのDisable（`disabled_manually` の実測）、歴史的run Re-runの遮断根拠、`--live` の実行成功、および最新HEADの必要CI確認。Issue #23はOpen、PRはDraftを維持する。

## 2026-10-14 再レビュー後のmain追従

- [PR #30の再レビュー](https://github.com/Fugu0141/Meltia/pull/30) で、`9b60616` 時点のコード・回帰テストに新たな必須修正がないことを確認した。残るP1は **GitHub管理者による旧workflow IDのDisable**、歴史的runのRe-runで既存Releaseを変更できない安全性の根拠、`--live` の実環境での成功である。
- PRの分岐元 `32ac40618b5298d1362a1a33b22ff602729b69ec` から `main` が28コミット進んでいたため、`5b55e7b42a97c99981bc2099f0f770a624db674c` を対象にPR専用ブランチへ統合する。競合するREADMEは最新mainのIssue #22・#32の参照を保持し、Issue #23のリンクを追加する。他のIssueで変更したファイルは上書きしない。
- **安全ゲート未完了**。公開タグ・Release/Assetsは変更せず、危険な旧workflowをDispatch/Re-runせず、PRはDraft/未マージ、Issue #23はOpenを維持する。
- 統合HEADの `legacy-linux-release-guard`（固定歴史SHA＋モック検証10件）、`meltia-strict-quality`、必要なLinux/Windows Build/PR checksはGitHub Actionsの同じHEADで区別して確認する。Draft/paths条件によるskippedを成功として扱わない。
