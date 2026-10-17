# R1b — 入力CaptureGate・再送・副作用順序の既存契約（2026-10-17）

> R1a（PR #54）の後続。**製品側のCaptureGate/CompositionController/OSフックのコードを変えず、既存動作だけを回帰テストに記録する。** 実Windowsキー入力やIME登録のE2Eではない。

基点：main `f8dfe4aa6aa73fee5a69bfa35482667057fba163`。[R1a仕様](MELTIA_R1_COMPOSITION_BEHAVIOR_CONTRACT_20261017.md) と [構造分割Issue #59](https://github.com/Fugu0141/Meltia/issues/59) を参照。

## 要件（R1bの受け入れ条件）

- Hookから`CaptureGate.OnKey/OnMouseButton`が受けた順番を、UI側`TryDequeue`・`Pump`または`Abort`が変更しない。
- 合成開始前のキー/マウスは捕捉しない。`TryRelease`はキュー未排出時に捕捉を解除しない。
- `OnKey`による同じキーのDown/Up、および修飾キーのキーアップを失わず、`SuspendInput`で保留入力を**1度だけFIFOで再送**する。
- 送信通知`_signal()`が失敗しても、既にキューにある保留イベントを`Abort`から取り出せる（**自動で復旧するという保証ではない**）。
- `ICompositionHost.ReplaceBackward`の継承デフォルト実装は**DeleteBackward→CommitText**の順であり、ホスト操作列を契約として凍結（実際のOSへの操作のatomicityは**非保証**）。
- Ctrl+Shift+Z / Ctrl+クリックは**確定→修飾キー再送→操作キー/クリック**の順で、必要なキーアップも渡す。既存Windowsのメッセージループ/競合テストは別途行う。
- `Reset`/`Abandon`はセッションの可変状態を破棄する。`CaptureGate.Abort`と`CompositionController.SuspendInput`の関係を明示し、全ての例外を自動処理するような未実装保証を行わない。

## 実装範囲

`src/Meltype.Core.Tests/MeltiaR1bInputOrderingTests.cs` は次の8つのcharacterization testを追加。

| テスト | 目的 |
| --- | --- |
| `R1b_Gate_DoesNotTakeNonStartOrMouseWhenIdle` | 非対象キー・マウスを通過 |
| `R1b_Gate_CapturesMixedDownUpMouseInStrictArrivalOrder` | Down→Up→Mouse→DownのFIFOとReleaseの可否 |
| `R1b_Gate_AbortPreservesInputOrderAndDoesNotReplayItself` | Abort結果の順序・一度きりの取り出し |
| `R1b_Gate_SignalFailureStillLeavesRecoverableQueuedInput` | UI signal例外後の保留・明示Abort |
| `R1b_SuspendInput_ReplaysPendingDownMouseUpInOrderAndOnlyOnce` | 停止時のCtrl等に依らない保留再送のFIFO |
| `R1b_ReplaceBackward_DefaultHostMethodDeletesBeforeCommit` | 既存インターフェースの削除→書込順序 |
| `R1b_Keyboard_ControlShortcut_CommitsBeforeReplayingAndPairsUp` | 修飾キー＋ショートカットの再送・確定順序 |
| `R1b_Keyboard_MouseCommitsBeforeCtrlAndClick` | Ctrl+クリックの確定と再送順序 |

## 確認方法

```sh
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release -- MeltiaR1b
dotnet run --project src/Meltype.Core.Tests/Meltype.Core.Tests.csproj -c Release
```

CIでCore・Windows Preview・品質テストを維持。実Windowsでの`SendInput`、パスワード欄、IME/フォーカス切替、例外時の再送可否は**R2/Windows実機専用**で、ユニット試験が通っても合格としない。

## 次段階

- **R1c**：UTF-16生文字列・途中ローマ字・Undo・URL/メール/結合文字について、原文・境界・再入力の同値性を追加。
- **R2 / #59**：固定された契約に沿って`CompositionController`の状態遷移とホスト側操作を段階的に分離。テストだけではIssue #59の受け入れ条件は満たさないので**Open維持**。
