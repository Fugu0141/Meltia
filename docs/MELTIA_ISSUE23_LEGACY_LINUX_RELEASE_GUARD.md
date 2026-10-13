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
- [ ] PR の回帰CI / Linux CI の結果を記録
- [ ] 過去 run の Re-run への運用上の制限・確認結果を記録
- [ ] `--live` による公開Release不変・旧workflow無効化の確認

**管理設定を実際に確認するまでは、過去refによるRelease書き換えが防止済みとは宣言しない。**
