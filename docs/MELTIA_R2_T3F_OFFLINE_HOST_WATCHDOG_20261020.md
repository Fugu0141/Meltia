# R2 / T3f — 無登録TIPホストの稼働監視と上限付き復旧（2026-10-20）

> **実験用ホストの短時間・明示起動ゲートに限る。** WindowsのTSF登録、有効化、キーイベント・TSF編集、Keyboard Previewからの自動起動、インストーラー・常駐プロセス配布を実装・実行しない。正式なMeltia IMEの完成を意味しない。

関連: [親Issue #16](https://github.com/Fugu0141/Meltia/issues/16) / [R2/T3e](MELTIA_R2_T3E_OFFLINE_HOST_LIFECYCLE_20261020.md) / [T4/T5ゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 1. 目的・要件

T3eはテストハーネスからホストをkillしたあと手動で再起動できることを示しただけで、**障害の自動検出や復旧制御**は未検証だった。T3fでは、監督役（supervisor）とホスト（worker）を責務分離し、次を独立プロセス間のWindows実試験で確認する。

1. `--supervise <sandbox>` の明示指定なしにはsupervisorを起動しない。保存先 `MELTYPE_DATA_DIR` は同じ一時sandbox内とし、本家・製品Previewのユーザーデータには触れない。
2. supervisorとworkerは別々のSID別mutexを持つ。同じSIDのsupervisor二重起動は拒否（コード3）。**自分が起動したworkerだけ**を停止対象とする。
3. 開始後はT3eの`ready`通知だけに依存せず、Meltia専用IPC `hello v1` の型と`active:true`を確認してから `supervisor.status` に現在のgeneration/worker PIDを公開する。
4. ホストプロセス終了を検知した場合は新しい別プロセスを起動する。プロセスが生きていても**パイプlistenerが停止**し、helloが連続2回失敗した場合は保有childを終了・起動し直す。
5. 失敗を無限に再試行せず、**最初の起動を含め最大3回**に制限。継続して失敗した場合はコード6で終了。短い待機を挟み暴走再起動を避ける。
6. `supervisor.stop` を受信したらchildに`stop`を送り、猶予内に終了しなければ自身のchildに限って強制停止。stopは復旧対象としない。
7. 起動に成功しても以前の入力中セッションを引き継がない。C#⇔Nativeの正常接続・変換の試験はT3d/T3eを継続し、T3fのヘルス確認自体はセッションを作らず`hello`のみ送る。

## 2. 基本設計

| コンポーネント | 責務 |
| --- | --- |
| `src/Meltia.TipHost.Experimental/OfflineSupervisor.cs` | 一時sandbox検査、supervisor排他mutex、worker起動・終了監視、定期hello、停止と上限付き復旧 |
| `OfflineProbe.IsHealthy` | 実`Meltia.Tip.v1.<SID>` pipeでhello送信。返信のversion、active、consumed、空commits、null viewを確認。例外・切断・期限超過は不健康として扱う |
| `OfflineHost` | T3eの既存起動を維持。別の引数で試験専用`health.drop`ファイルを監視し、**プロセスを残したまま`TipServer.Dispose()`する**故障注入を追加 |
| `Program.cs` | `--supervise <sandbox>` をopt-in追加。無引数実行や通常のMeltia.exeへの配線なし |
| `tools/test-meltia-t3f-supervisor.ps1` | Windows別プロセスで「正常接続→二重supervisor拒否→listener消失→回復→workerクラッシュ→回復→正常停止」及び別supervisorで3回停止→code6 |
| `tools/test_meltia_t3f_supervisor_contract.py` | sourceとCI上の禁止API・opt-in・試験の存在を回帰確認 |
| `.github/workflows/meltia-windows-preview-ci.yml` | T3e後にT3f実Windows試験を追加（x64ホスト）。既存T3dのx64/x86 native IPC回帰は維持 |

### 監視・復旧時の状態遷移

1. **Start**: 一時パスとSID確認 → exclusive supervisor mutex → 自身の子`dotnet ... --offline-host`を起動。
2. **Ready**: childの`ready`ファイルを受信し、実IPC helloが通ることを検証 → 新しい`launch=N;pid=P`を`supervisor.status`に原子的なrenameで発行。
3. **Healthy**: 300ms周期で生存確認とhello-only health check。新しいIPC接続は既存の作業セッションを操作しない。
4. **Unhealthy**: 子の異常終了、または連続2回health不成立 → 所有する子だけを停止 → 200ms×試行番号の待機 → 新規起動。前世代の入力状態は引き継がない。
5. **Exhausted**: 3回の起動を使い切った場合はcode6で停止。無制限の再実行や全システム再起動はしない。
6. **Intentional stop**: supervisorに`supervisor.stop`が作られた場合はcode0で正常終了、childも停止する。終了要求による停止をクラッシュと誤検知しない。

## 3. 受け入れ条件と試験

Windows / .NET 10 / Pythonで実行:

```powershell
python tools/test_meltia_t3f_supervisor_contract.py
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3f-supervisor.ps1
```

- 有効な一時sandbox外の要求はcode2を返し、Pipe・子・ユーザー保存先に影響しない。
- 起動完了状態はreadyファイル**と本当のhello応答の両方**で判定する。
- 同じSIDの2つ目のsupervisorはcode3、稼働中の第1supervisorには影響なし。
- `health.drop`で生きたhostのlistenerだけを止め、2回の失敗後に子がkillされ、新プロセスに交代して`--probe`で「か」の生成・確定・closeが成功する。
- workerを直接killしても新世代のプロセスで実IPCが再開する（古いセッションは非継承）。
- supervisorの停止要求でcode0、子も終了、Pipeを通したprobeが失敗する。
- 別のsupervisorでworkerを連続3回終了させ、4回目は起動せずcode6となる。
- T3e、T3d、Preview、Core研究品質、Strict Qualityが回帰しない。

**CI実測欄:** 実装レビュー前のWindows CIとStrict Quality結果を後で記録する。静的Python検査とWindows実通信・実プロセス試験を混同しない。

## 4. 非対象・制限事項

- **製品版の常駐サービスにはしない。** 本supervisorはTest用CLIを明示起動した場合のみ。Windowsログオン時自動起動・タスクスケジューラ・サービス・レジストリ操作を追加しない。
- ヘルスはv1サーバーが応答するかを示すだけで、Windows TSFのキー配信、アプリ編集、入力消失・重複、画面フォーカスや変換辞書品質の成功証明ではない。
- ヘルス接続は同じMeltia専用Pipeを使用するので、通信障害時の応答遅延や負荷上限の詳細検討は次工程。再起動時は入力途中のセッションを**復元しない**。
- supervisorが予期せず強制killされた場合に孤児childをカーネルレベルで確実に終了させる仕組み（Job Object等）は**未実装**。製品プロセス管理に移行する際の別ゲートにする。
- 手動開始された既存experimental workerは取得・破壊しない（worker mutex競合なら新規作成分だけ失敗→上限停止）。
- 本家Meltypeのコード・Native TIPの登録系と通常Meltia Keyboard Previewの起動/Mutexは一切変更しない。正式T4/T5は隔離Windows VMおよび独立レビュー必須。
