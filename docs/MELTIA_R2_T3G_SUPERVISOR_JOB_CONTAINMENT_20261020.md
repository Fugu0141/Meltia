# R2 / T3g — 監視プロセス強制終了時の実験worker回収（2026-10-20）

> **試験用・無登録ゲート。** T3fの明示 `--supervise`（またはCI限定の `--supervise-test-assign-failure`）以外からはsupervisorを起動せず、正式TSF/IME登録、キー入力、Windowsサービス化、インストーラー、自動起動には接続しない。Windows 10/11実機のIME挙動はこの工程で検証しない。

関連: [親Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [T3f](MELTIA_R2_T3F_OFFLINE_HOST_WATCHDOG_20261020.md) / [T4/T5ゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 1. 背景と要件定義

T3fでは通常停止・workerの死活監視・IPC listenerの脱落復旧・最大3回の再起動を確認した。しかし、**supervisor自身が強制終了されると、既に起動したworkerだけが残る**可能性がある。T3gではこの一件を修正し、その他の起動・変換・復旧契約を変えない。

- **R1:** supervisorが自分で起動したworkerだけを所有し、子をWindowsの試行ごとに生成した一意な名前付きJob Objectに割り当てる。
- **R2:** `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` を設定し、supervisorがkillされ、workerの短時間の所属検証用ハンドルも閉じて最後のJobハンドルが消えたときにworkerが終了する。
- **R3:** supervisor管理下のworkerがlistenerを開くのは**Job割り当て成功と正しいJob所属の確認後**に限る。割り当て失敗ではfail-closedとしてsupervisorを失敗終了させ、未収容のworkerを停止する。
- **R4:** 割り当て前にsupervisorがkillされてもworkerが無期限に待機・実通信することはない（起動ゲート待機10秒でコード7）。
- **R5:** 手動停止・listener断・worker kill・3回の起動上限・別SID/workerの不干渉をT3fのまま維持する。
- **R6:** 同じWindowsユーザーの既存のMeltype/Keyboard Preview、保存ディレクトリ、レジストリ、TSF/TIPに触れない。

## 2. 基本設計

| ファイル | 所有権・責務 |
| --- | --- |
| `OfflineWorkerJob.cs` | `CreateJobObjectW` で**試行ごとのランダムな一意名**を持つJobを生成。`KILL_ON_JOB_CLOSE`を設定し、自身が起動したProcessハンドルだけを割り当てる。workerは `OpenJobObject(JOB_OBJECT_QUERY)` と `IsProcessInJob` で**このJob**への所属を確認し、照会ハンドルを即時閉鎖する |
| `OfflineSupervisor.cs` | 試行開始前にJob作成→`Process.Start`→`worker.started`へPID記録→Job割り当て→`start.allowed`発行→従来のready + 実hello検査。各試行ごとにJobを破棄 |
| `OfflineHost.cs` | Supervisor専用の**6引数形式**（ready/stop/drop/gate/jobName）はstart gateとJob所属の双方を確認してからmutex/pipeを作る。gateなしの旧T3e CLIは維持 |
| `Program.cs` | T3g workerの追加引数を明示的に選択し、CI専用の割り当て失敗注入 `--supervise-test-assign-failure` を隔離。既定起動は従来どおり拒否 |
| `tools/test-meltia-t3g-job.ps1` | 実Windowsプロセスを用い、正常IPC→supervisorを強制kill→worker自動終了→新監視プロセスで再接続→意図的停止、および偽gate・実workerを起動した割り当て失敗注入を検証 |
| `tools/test_meltia_t3g_job_contract.py` | Jobの一意名・非継承・kill-on-close・正しいJobへの所属検証・割り当て失敗注入とCI結合を静的に確認 |

### 失敗・レースの扱い

- Jobの**作成・設定・割り当てに失敗した場合は、保護がないまま継続しない**。CI限定引数で`Process.Start`後かつ`Assign`前に例外を注入し、`worker.started`のPIDが終了・ready未生成・IPC不成立・supervisor非0終了を検証する。起動したworkerは既存`StopChild`のfinallyで停止する（割り当て前なのでそのworkerはlistenerを開いていない）。
- `Process.Start`直後からJob割り当てまでの瞬間はOSレベルで原子的ではない。代わりに、workerは`start.allowed`と**自分が指定されたJobに所属すること**の両方を満たすまで**IPCを起動せず**、監督役が突然死した場合も10秒後に自発終了する。これは`CREATE_SUSPENDED`を使った原子的なプロセス生成と同義ではない。
- Jobのハンドルを子へ継承せず（workerは一時的な照会ハンドルだけ開く）、別ユーザー/別プロセス・既存workerをJobに取り込まない。既存workerが原因でSID mutexが競合する場合も採用せず失敗する。
- Windows側でJobへの割り当てが拒否される実行環境（既存Jobの制約等）はサポート成功と見なさず、実Windows CIで失敗を観測して対応を別途判断する。
- worker再起動は従来の3回上限。再起動後の入力中セッションは引き継がない。
- Jobは**supervisorによる管理対象の直接worker**に限った回収保証を試験する。製品全体、複数子プロセス、異常時のデータ復元まで保証しない。

## 3. 受け入れテスト

```powershell
python tools/test_meltia_t3g_job_contract.py
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3g-job.ps1
```

1. Windows + .NET 10の実別プロセスで`--supervise`起動、`supervisor.status`にhealthyなworker PID、v1 Pipeで「か」確定/closeが成功。
2. supervisorを`Kill()`し、10秒以内に実workerも終了する。workerの強制停止は**試験側が行うのではなくOSのJob close**が行う。死亡後はIPC接続不能。
3. 同SIDで新しいsandboxからsupervisor起動・v1 hello/入力試験成功。正常stop後に新workerも回収。
4. Job未割当の6引数workerはgateなしで10秒以内にコード7となりready未生成。**事前作成された偽gate**を渡してもJob所属がなければコード7でIPCを開かない。
5. CI専用割り当て失敗注入（実workerを起動しJob割り当て前に例外）でsupervisor非0終了、記録されたworker PID終了、ready/status未生成、IPC接続不能。
6. 既存のT3a〜T3fの試験、Preview無フック・Strict Qualityを維持する。

## 4. 検証状況

**実装時の区別:** このMarkdownに記載した設計や静的検査はWindows Jobの実動作証明ではない。PRのGitHub Actionsで上記シナリオの実測を確認してからMergeを判断する。Windows実機・TSF登録/打鍵E2Eは別工程T4/T5で実施する。

## 5. PR #87 レビューP2への対応（2026-10-20）

- **起動ゲートの偽装:** 旧実装はファイル存在のみでlistenへ進めた。ランダム一意Job名を親がworker起動引数に渡し、worker側で`OpenJobObject(JOB_OBJECT_QUERY)`→`IsProcessInJob`による所属検証を必須化。Job名が不存在、または所属が違えばcode7。ファイルの事前作成だけでは通過しない。
- **割り当て失敗の実試験:** `--supervise-test-assign-failure` で親が子を本当に起動してPIDを記録したあと例外を発生させる。これにより既存の`finally StopChild`が回収できることと、パイプが開かれていないことをWindows CIで判定する。これは**人工的な例外による後処理試験**であり、OSから本当に`AssignProcessToJobObject`が失敗する特殊環境の完全再現ではない。
- **残余制約:** Job識別子とgateは同一ユーザーのテスト用プロセスへ公開されるため、悪意ある同一ユーザープロセスに対する認証境界ではない。任意起動の旧`--offline-host`（T3e互換）は従来どおりJob保証なし。あくまでsupervisor管理下のT3g workerに対する契約である。
- **検証段階:** 正式な合否は修正HEADのWindows Actions（T3g Windows実プロセス試験）、既存T3e/T3f、Strict Qualityを確認してから追記する。
