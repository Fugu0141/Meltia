# R2 / T3g — 監視プロセス強制終了時の実験worker回収（2026-10-20）

> **試験用・無登録ゲート。** T3fの明示 `--supervise` 以外からは起動せず、正式TSF/IME登録、キー入力、Windowsサービス化、インストーラー、自動起動には接続しない。Windows 10/11実機のIME挙動はこの工程で検証しない。

関連: [親Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [T3f](MELTIA_R2_T3F_OFFLINE_HOST_WATCHDOG_20261020.md) / [T4/T5ゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 1. 背景と要件定義

T3fでは通常停止・workerの死活監視・IPC listenerの脱落復旧・最大3回の再起動を確認した。しかし、**supervisor自身が強制終了されると、既に起動したworkerだけが残る**可能性がある。T3gではこの一件を修正し、その他の起動・変換・復旧契約を変えない。

- **R1:** supervisorが自分で起動したworkerだけを所有し、子をWindowsの匿名Job Objectに割り当てる。
- **R2:** `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` を設定し、supervisorがkillされて唯一のJobハンドルが閉じられたときにworkerが終了する。
- **R3:** workerがlistenerを開くのは**Job割り当て成功後**に限る。割り当て失敗ではfail-closedとしてsupervisorを失敗終了させ、未収容のworkerを停止する。
- **R4:** 割り当て前にsupervisorがkillされてもworkerが無期限に待機・実通信することはない（起動ゲート待機10秒でコード7）。
- **R5:** 手動停止・listener断・worker kill・3回の起動上限・別SID/workerの不干渉をT3fのまま維持する。
- **R6:** 同じWindowsユーザーの既存のMeltype/Keyboard Preview、保存ディレクトリ、レジストリ、TSF/TIPに触れない。

## 2. 基本設計

| ファイル | 所有権・責務 |
| --- | --- |
| `OfflineWorkerJob.cs` | `CreateJobObjectW` で**名前なし**のJobを作成。拡張制限情報に`KILL_ON_JOB_CLOSE`を設定し`AssignProcessToJobObject`で自身のProcessハンドルのみ登録する。`SafeFileHandle`でJobハンドルを確実に閉じる |
| `OfflineSupervisor.cs` | 試行開始前にJob作成→`Process.Start`→Job割り当て→`start.allowed`発行→従来のready + 実hello検査。各試行ごとにJobを破棄 |
| `OfflineHost.cs` | Supervisor専用の5引数形式だけ、start gateを待ってからmutex/pipeを作る。gateなしの旧T3eのCLIは維持 |
| `Program.cs` | T3g workerの追加引数を明示的に選択する。既定起動は従来どおり拒否 |
| `tools/test-meltia-t3g-job.ps1` | 実Windowsプロセスを用い、正常IPC→supervisorを強制kill→workerが自動終了→新しい監視プロセスで再接続→意図的停止を検証 |
| `tools/test_meltia_t3g_job_contract.py` | Jobが無名・非継承・kill-on-closeで、未割り当てworkerを起動しないこととCI結合を静的に確認 |

### 失敗・レースの扱い

- Jobの**作成・設定・割り当てに失敗した場合は、保護がないまま継続しない**。起動したworkerは既存`StopChild`のfinallyで停止する（割り当て前なのでそのworkerはlistenerを開いていない）。
- `Process.Start`直後からJob割り当てまでの瞬間はOSレベルで原子的ではない。代わりに、workerは`start.allowed`まで**IPCを起動せず**、監督役が突然死した場合も10秒後に自発終了する。これは`CREATE_SUSPENDED`を使った原子的なプロセス生成と同義ではない。
- Jobのハンドルを子へ渡さず、別ユーザー/別プロセス・既存workerをJobに取り込まない。既存workerが原因でSID mutexが競合する場合も採用せず失敗する。
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
4. Job未割当で起動した5引数workerはreadyを発行せず、10秒でコード7終了する。
5. 既存のT3a〜T3fの試験、Preview無フック・Strict Qualityを維持する。

## 4. 検証状況

**実装時の区別:** このMarkdownに記載した設計や静的検査はWindows Jobの実動作証明ではない。PRのGitHub Actionsで上記シナリオの実測を確認してからMergeを判断する。Windows実機・TSF登録/打鍵E2Eは別工程T4/T5で実施する。
