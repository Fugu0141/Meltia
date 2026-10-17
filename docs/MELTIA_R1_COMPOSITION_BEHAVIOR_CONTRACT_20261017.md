# R1 — Meltype継承Compositionの動作契約を凍結する（2026-10-17）

> 状態：既存動作を変更しないテスト・文書の提案。**Meltype v1.2.1の修正・Meltia Core既定の境界モデル切替・Windows TSF登録は対象外**。

調査基点：Meltia main `6ee54499c656b76d935ff4093b522e57cc5879b8`。上流との比較は [R0ファイル台帳](MELTIA_MELTYPE_V121_R0_FILE_INVENTORY_20261017.md)と[145コミット履歴](MELTIA_MELTYPE_V121_R0_COMMIT_INDEX_20261017.md)を使用する。[上流受け入れ方針](MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md)に従い**挙動不変の抽出PRと機能変更を分離**する。

## 1. 現行コードの責務・境界（固定する事実）

| レイヤー | 現在のパス | 現行責務・次に固定すべき契約 |
| --- | --- | --- |
| 打鍵の捕捉とキュー | `src/Meltype.Core/Composition/CaptureGate.cs`、`Input/KeyEvent.cs` | キーのDown/Upの対応、保留/解放/Abort、再送の順序 |
| 逐次文字データ | `CompositionText.cs` | `CompositionUnit(Kana,Raw)`・`Pending`・`Raw`。元入力文字列の保持、部分ローマ字、Delete/BackSpace、`Segments(final)` |
| 区間判定 | `CompositionDetector.cs` | Meltypeの語・ローマ字・保護区間と`BoundaryRefinement`。MeltiaはOptional Adapter経由でのみ追加する |
| 状態・編集 | `CompositionController.cs`／`CompositionController.Conversion.cs`／`CompositionController.Learning.cs` | `Pump`（UIスレッド）、`CommitPending`、`Abandon`、`SuspendInput`、`Reset`、`ICompositionHost`。文節選択、確定/再送、削除と補正、学習に副作用あり |
| 入力先ホスト | `ICompositionHost`（`CompositionController.cs`） | `CommitText`、`Replay(KeyEvent/MouseButtonEvent)`、`DeleteBackward`／`ReplaceBackward`、`Show`/`Hide`、前後文脈の取得。OSごとに別実装 |
| 統一セッション | `MeltypeSession.cs` | `HandleKey`が `SessionResult(Consumed, Commits, View)`を返す。1スレッドの同期呼出。Appが未消費キーをそのまま扱い、`TextEdit(DeleteBefore,Text,Expect)`で補正を表す |
| Windows Keyboard Preview | `src/Meltia.Windows` + 継承`src/Meltype` | keyboard hook+replayは`MeltypeSession`ではなくWindows既存経路。**Coreセッションの合格だけではKeyboard実機/E2Eを保証しない** |
| 正式TIP候補 | `native/tip` + `src/Meltype/Tip/TipServer.cs` | SessionResultをJSONにしPipe越しに返す。未登録・本家固有の現状。T1-B契約調査で別管理 |

`ICompositionHost`は既に外界副作用の境界を提供する。新規interfaceを大量に増やすより、**先に既存のメソッドと順序・例外時挙動をテストで固定**する。現行`CompositionController`は1,393行、`CompositionText`は1,282行、`MeltypeSession`は467行であり、`partial`への分割だけでは共有状態は解消されない。

## 2. 追加するcharacterization tests

`src/Meltype.Core.Tests/MeltiaR1CompositionContractTests.cs` に`[Test]`形式で追加。既存の`Meltype.Core.Tests`テストランナーが自動列挙する。全ケースは仮のホストと純粋なJSON DOMを使用し**フック・COM・レジストリ・実ユーザーデータへのアクセスなし**。

| 新規ケース | 不変条件と失敗時リスク |
| --- | --- |
| `R1_JsonCommitSchema_PreservesUnicodeAndNoExpectWithoutDelete` | Unicode・引用符・改行の往復、追加だけの編集には`expect`を出さない |
| `R1_JsonCorrectionSchema_RequiresExactExpect` | `DeleteBefore>0`時の`expect`がJSONで欠落・化けない。**nativeが本当に比較することの証明ではない** |
| `R1_PassThrough_DoesNotCommitOrClearActiveComposition` | `Consumed=false`経路で勝手に確定せず、表示・未確定状態を保持 |
| `R1_CommitForPassThrough_FlushesOnlyOnceAndPassesOriginalKey` | 既存未確定文字の確定と、元OSキーのパススルーを1回ずつ行うための境界。二重確定なし |
| `R1_CommitPending_FlushIsIdempotent` | 複数回のfocus/commit通知によって同じ文字が重複しない |
| `R1_AbandonAfterFocusLoss_NeverCommitsIntoNewTarget` | 別入力先へ移動後の未確定文字は**新しい入力先へ書かない**。入力消失防止とは別の安全要件 |
| `R1_ExplicitBypass_WhenIdle_IsNeverACommit` | 未使用キーはConsumed=false、Commitなし、Viewなし |

注記：これらは**新しい望ましい仕様を勝手に定義するのではなく、調査時の継承コードを凍結する試験**。特に`Abandon`はフォーカス移動時の**安全側破棄**であり、「すべての打鍵を必ず保存」とは混同しない。キー再送（KeyDown/Up）、文節操作、Meltia CoreのTop-K/UTF-16、学習・辞書は既存テストも継続使用する。

## 3. 次に純粋な責務分割を進める順序

1. **R1a（本PR）**：公開された`MeltypeSession`と`ICompositionHost`周辺のcommit/pass-through/focus/JSON契約を凍結し、CIで既存回帰を走らせる。
2. **R1b（後続）**：`CaptureGate`の入力Down/Up・Abort/Replayの全順序と例外後のリセット、`ICompositionHost.ReplaceBackward`の削除・再書込の副作用を偽ホストのイベント履歴で比較。
3. **R1c（後続）**：`CompositionText.Raw`／`Pending`／`Segments`の全UTF-16原文保存、emoji・結合文字・保護URL/メール・学習メモリ/辞書の変換パターンを既存Meltiaブリッジと検証。
4. **R2（別PR）**：テストで守れた純粋変換や入力状態の処理だけ抽出。移植・リファクタリング・スコア変更を混ぜない。

## 4. 受け入れ条件と実施しない項目

- `dotnet run --project src/Meltype.Core.Tests -c Release -- MeltiaR1`でR1の独立テストが合格。全継承Core／Meltia Bridge／Quality testsとWindows Preview CIも維持。
- 既存ソース`src/Meltype.Core/Composition`・`native/tip`・`src/Meltia.Core`・Windows起動・辞書や設定を**一切変更しない**。
- 本家への追従、`InputMode.Tsf`の切替、設定・辞書の上書き、installer/COMレジストリ操作、Windows実入力検証はこのPRの完了条件に含めない。
- CIが成功しても、現行TIPがMeltypeから独立したという意味ではない。IME用IPC、フォーカス・文字の整合性はT1-Bと実Windows E2Eで別検証する。
