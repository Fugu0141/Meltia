# R2 / T3e — Meltia専用・無登録ホストのライフサイクル検証（2026-10-20）

> **正式なMeltia IMEの完成・OS登録・実打鍵の証明ではない。** T3dまでで確認した実C#⇔Native IPCサーバーについて、明示指定でのみ起動する**試験用別プロセス**を作り、二重起動・終了・クラッシュ後再起動を Windows CI で確かめる。配布物・自動起動対象には含めない。

関連: [親Issue #16](https://github.com/Fugu0141/Meltia/issues/16)、[T3d](MELTIA_R2_T3D_REQUEST_E2E_20261020.md)、[T3b](MELTIA_R2_T3B_ISOLATED_IPC_20261018.md)、[T4/T5独立レビューゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)。

## 1. 今回の要件

1. 継承した Keyboard Preview の起動・フック・自動更新を変更せず、別の .NET Windows 実行プロジェクトを用意する。`--offline-host <ready> <stop>` を明示しなければサーバーを立ち上げない。
2. Windows SID 別のMeltia実験用mutex（本家 `Local\Meltype.SingleInstance` とは別）により二重起動を拒否し、競合したプロセスは既存ホストを終了させない。
3. Windows Named Pipe の**最初の実インスタンスが生成されたこと**を待ち、失敗時にreadyを偽装しない。これにより `TipServer.Start` のfire-and-forget後すぐに成功を報告してしまう競合を回避する。
4. 設定はメモリ内だけ `InputMode.Tsf` にして通信契約を駆動し、OSのIME選択・COM/TSF登録・キーボードフックを実行しない。辞書や学習データの品質評価はしない。
5. テスト用 `MELTYPE_DATA_DIR`・ready/stopファイルは Windows Temp 配下の専用ディレクトリのみ許可。本家 `%LOCALAPPDATA%\Meltype` へ暗黙フォールバックしない。設定ファイルも生成しない。
6. 正常終了後のパイプ切断と、プロセス強制終了後のWindowsカーネルによるmutex/パイプの解放を別プロセスで検証する。新ホストではセッションを引き継がない。
7. 既存T3a〜T3d、Previewの無フック試験および研究品質をCIで維持する。

## 2. 基本設計と改修対象

| 対象 | 責務 |
| --- | --- |
| `src/Meltia.TipHost.Experimental/*.csproj` | `Meltype`アセンブリから実 `TipServer`/`MeltypeSession` を参照する**試験専用**ホスト。Meltia.exe/インストーラーには含まれない |
| `Program.cs` | `--offline-host` と `--probe` 以外はコード2で拒否。自動起動も設定永続化も行わない |
| `OfflineHost.cs` | Windows SID専用mutexの即時獲得、Tempパス検査、メモリ内Tsf設定、STAのWinForms message loop、明示stopとタイムアウト（60秒）、mutex解放 |
| `OfflineProbe.cs` | 別プロセスからMeltia専用Pipeに接続してhello→k/a→「か」→commit→closeを型付きJSONで確認。既存の実 `MeltypeSession` を駆動 |
| `src/Meltype/Tip/TipServer.cs` | **既存Start/Serve処理は変更せず**、最初のpipe生成成功時に `FirstListenerReady=true`、pipe生成前にAcceptLoopが終了すると `false` を通知する内部プロパティを追加（通常の本家/Previewホストには未使用） |
| `src/Meltype/Meltype.csproj` | Internal API の参照先を**実験用アセンブリだけ**追加 |
| `tools/test-meltia-t3e-host.ps1` | 新プロセス→本物のIPC→二重起動拒否→正常終了→パイプ解放→クラッシュ→再起動を検証し、必ず子プロセスとTemp生成物を片付ける |
| `tools/test_meltia_t3e_host_contract.py` | 本家Previewに接続されていないこと、禁止操作が混入していないこと、ready通知の実装位置などの静的回帰 |
| `.github/workflows/meltia-windows-preview-ci.yml` | Windowsジョブで上記試験を実行、関連ファイルでトリガー |

### 実装上の意図的な制限

- **既存の Keyboard Preview のmutexには触れない。** この実験ホストはキーフックもTSF登録も行わないので、別起動しても二重入力経路にならない。将来の本格TSF有効化時には、Previewとの排他とモード切替を**別PR**で実装・検証する。
- `NullConverter` を用いるため、かな漢字変換・外部辞書・学習永続化は検証しない。`CompositionService` を生成しない（フックやWindows側操作と混同しない）。
- Windows Tempパス制限は**CI用ホストの誤使用防止**であり、悪意あるローカルプロセスに対するサンドボックスではない。製品レベルのデータパス設計は別途必要。
- ホストの `Tsf` は TipServer の `active` 条件を再現するためだけの**インメモリフラグ**。Windowsの既定IMEモードやプロファイルを変更しない。
- `FirstListenerReady` は**最初の待受インスタンス生成**の成功条件。稼働中の全接続処理や復旧状態を恒久的に監視・保証するものではない。サーバーの永久停止時の親プロセス監視は次工程。

## 3. テストと受け入れ条件

Windows + .NET SDK 10 / Python:

```powershell
python tools/test_meltia_t3e_host_contract.py
powershell -ExecutionPolicy Bypass -File .\tools\test-meltia-t3e-host.ps1
```

| シナリオ | 期待 |
| --- | --- |
| 引数なし起動 | コード2、ホスト生成なし |
| Temp外のデータパス | コード2、`%LOCALAPPDATA%\Meltype` に触れない |
| プロセスAの起動 | 専用mutexと初回パイプ実待受取得後、readyファイル生成 |
| IPC probe A | 別プロセスの実TipServerでhello、k/a→「か」、commit（deleteBefore=0）、closeが成功 |
| プロセスBの二重起動 | コード3、ready生成なし、Aの通信に影響なし |
| プロセスAの正常終了 | コード0、パイプ消滅、後続probe失敗 |
| プロセスCの起動・強制終了 | Cで実IPC成功後、意図的にKill |
| プロセスDで復旧 | 同じSIDとパイプ名で新規起動・probe成功。新セッションは引き継がれない |
| CI | T3a〜T3d、Strict Quality、Previewビルド・無フック監査が継続成功 |
| 禁止事項 | `regsvr32`、COM/TSF登録、IME切替、キー捕捉、永続設定、自動起動、実環境へのインストールは未実行 |

**Windows CI実測**: PR作成後にActionsで確認し、成功/失敗とコミットSHAをここへ追記する。Linuxの静的試験のみでWindowsの実プロセス間IPC成功とは判断しない。

## 4. 次工程・未達事項

- この段階では**手動・短時間の試験専用プロセス**だけ。製品の常駐プロセス、起動時登録、バックグラウンド復旧管理、既定アプリモード切替は作らない。
- 今回のCLIとready/stopファイルはCIの可視化用であり、製品の制御API/認可設計ではない。プロセス間の終了要求、競合プロセス所有者の確認、本家との共存等は別に検証する。
- TSF登録/解除、`TextService::OnKeyDown`・`Apply`を含むWindows OS実入力と権限差の試験は、**レビュー後のT4/T5専用Windows VM**を必須とする。
- Windows PreviewはKeyboard固定で維持する。実験ホストからPreview本体への配線は作らない。レビュー未完了時はDraft PRを維持する。
