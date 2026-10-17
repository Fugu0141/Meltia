# T1-B — 継承TIPのC#／C++名前付きパイプ通信契約（2026-10-17）

> **設計資料・ソース読取テストのみ。Meltia専用TIPは未実装、現行`native/tip`はMeltype固有であり、登録・配布・起動しない。** この文書は現行Meltype継承コードの実装観察を記録するもので、将来のMeltia専用プロトコルを承認・保証するものではない。

比較時のMeltia main: `6ee54499c656b76d935ff4093b522e57cc5879b8`。[T0〜T6の独立IMEゲート](MELTIA_TSF_IME_READINESS_AND_VERSION_GATE_20261017.md)と[Meltype上流採用ルール](MELTIA_UPSTREAM_MELTYPE_INTAKE_AND_REFACTORING_POLICY_20261017.md)に従い、Meltia PreviewのKeyboard既定動作を変更しない。

## 1. データ・呼び出し経路（現状）

```text
Windows TSFホストアプリ (本家ネイティブTIP、Meltia用ではない)
  native/tip/TextService.cpp  [Request; synchronous edit session]
     | JSON UTF-8 / 1 message per key / named pipe per Windows user SID
     v
  native/tip/Pipe.cpp [Connect, timeout, response fragments, peer checks]
     v
  src/Meltype/Tip/TipServer.cs [NamedPipeServerStream / per-connection sessions]
     | process/active/moved/composing/session id
     v
  CompositionService.CreateSession() -> MeltypeSession.HandleKey() [UI thread]
     | SessionResult(Consumed, Commits, View).ToJson()
     v
  TipServer reply {"active":..., "consumed":..., "commits":[...], "view":...}
     |
     v
  native/tip/TextService.cpp [Apply, ExtendOverPrevious, ApplyComposition,
                             ShowCandidates, Reinject]
```

**境界は2種類ある**：`ICompositionHost`（処理内の副作用）とPipe JSON（Windows TSFプロセス間）。`MeltypeSession`の`Consumed=false`は**Native TIPが元のキーを通す**ことを意味し、`active=false`はTIPサーバーが処理を引き受けない。既存Keyboard Previewは`InputMode.Keyboard`固定で、上記の正式TSF経路を利用していない。

## 2. 現行の要求（C++生成 → C#受信）

| op | 要求・フィールド | サーバーの扱い | ネイティブの扱い |
| --- | --- | --- | --- |
| `hello` | `{"op":"hello","process":"notepad"}` | `ActiveFor(process)`を確認し`active`を返す。変換をしない | TIPのサーバー活動状態を確認 |
| `key` | `{"op":"key","sid":"id","vk":65,"ch":97,"mods":1,"process":"notepad","before":"...","after":"...","moved":true,"composing":true}` | `sid`ごとの`MeltypeSession`を取得/開始、`moved`なら以前の確定を忘れ、`HandleKey(vk,ch,shift,ctrl,alt,command,before,after)`を同期実行。初回`composing=true`ならセッション作り直しを拒否 | Key入力→返信に応じcommit・表示・再送 |
| `commit` | `{"op":"commit","sid":"id","moved":true}` | 未確定を`CommitPending`。移動後は`ForgetLastCommit` | フォーカス変更・明示確定 |
| `select` | `{"op":"select","sid":"id","index":2}` | 対象セッションの候補を選択 | 候補選択の非同期edit session |
| `close` | `{"op":"close","sid":"id"}` | その接続のセッションを破棄。未確定があれば`CommitPending`を呼ぶが**生成された返信は使用せず`Inactive`を返す** | 接続終了通知。**closeに確定内容の応答が戻る前提にしてはならない** |

- `sid`はNativeで生成し、サーバーは**接続単位のDictionary**に保存。接続間で再利用しない。
- `ch`は現行C#で`0<ch<65536`のみ`char`へ変換（単独サロゲートなどの扱いに注意）。非BMP等を変換したいときは、`MeltypeSession.CommitForPassThrough`等の安全な経路とOS側の元イベントを別途評価。
- `mods`のbitは`1=Shift`、`2=Ctrl`、`4=Alt`、`8=Windows/Command`。Nativeの`KeyDown`から生成。
- `before`/`after`はTSF入力欄の周囲テキスト。初回等、**変換中でないときのみ**付ける。必須/常に最新とは扱わない。
- `moved`はキャレットが動いた可能性の印。確定し直しで別の文字を削除しないために使う。`composing`はNative側でまだ未確定が残るときのみ送られる。
- Nativeの`Request`は自プロセスにいる場合、デッドロック回避のため`tid`を追加する特殊処理がある。これは**現時点の本家`Meltype.exe`想定**なのでMeltia化時に要再設計。

## 3. 現行の応答（C#生成 → C++適用）

| JSONフィールド | 現行形式・安全契約 |
| --- | --- |
| `active` | bool。falseならサーバーは入力を受け持たない。inactive固定形：`{"active":false,"consumed":false,"commits":[],"view":null}` |
| `consumed` | bool。`false`では元キーを通す必要がある。Nativeは`Reinject(vk)`または失敗時の直接挿入経路を選び、**二重挿入・文字消失の両方**を防ぐ |
| `commits` | `[{"deleteBefore":0,"text":"..."}, ...]`。配列要素を**順番どおり**適用。前文字削除時のみ`expect`が設定される場合がある |
| `view` | `null`または`{"text":"...","converting":false,"selectedIndex":-1,"selectedClause":-1,"hint":"...","candidates":[],"clauses":[],"suggestion":null,"meaning":null,"notes":[]}`。Nativeは文節／候補に反映し、不要時に閉じる |
| `deleteBefore` + `expect` | C#の`TextEdit`は、前に確定した文字列が分かる場合に削除期待値を付ける。Nativeの`ExtendOverPrevious`は指定数だけ遡れて、読み出した文字列が`expect`と一致する場合にだけ置換対象を広げる。**期待値が読めない・違う場合は削除しない** |

**重要な例外と検証論点**：`expect`は`TextEdit`ではnullable。C#単体のJSON検査だけではNative側で安全に削除されることを証明しない。また、`active=false`・`consumed=false`の処理、非同期編集のキャンセル、ホストアプリの予期せぬ編集などを、実Windowsにて確認する必要がある。

## 4. サイズ・時間・失敗時動作（現コード定数）

| レイヤー | 上限と挙動 |
| --- | --- |
| `TipServer.Serve` | C#側のリクエスト上限 `MaxRequestBytes=64*1024`、一接続の`MaxSessionsPerConnection=16`、全体`MaxConnections=1024` |
| `PipeClient.Transact` | C++側の返信上限 `kMaxResponse=1024*1024`。複数の読み取りで組み立て、上限超過なら失敗 |
| `TextService.Request` | `pipe_.Transact(request,response,800)`、応答をJSONパースできなければfalse。**800msは現行コードのタイムアウト指定であり将来の性能要件ではない** |
| `TipServer.Serve` | JSON例外などはログ後`Inactive`で応答。リクエスト過大、Pipe断、停止・待機時も接続終了 |
| `TextService.HandleKey` | 応答がなければ編集中の内容を安全側で確定し、状況に応じて元のキーの直接挿入または`Reinject`。再送ループには抑止処理がある |
| 接続の認可 | SIDを含む本家専用Pipe名、Pipeセキュリティ・接続先の確認・整合性レベル/権限差の処理。**現在の本家実装のセキュリティ審査を完了したという意味ではない** |

## 5. R1からIME独立化までの採用ゲート

1. **R1の既存`SessionResult`とhost副作用契約**を先に固定し、`Consumed`・`CommitPending`・`ForgetLastCommit`・原文/UTF-16を検査。
2. IPCスキーマの**自動チェック**：`tools/test_meltia_tip_ipc_contract.py`。C#受信とNative生成の5操作、応答必須フィールド、前文字削除時の期待値参照、通信サイズ上限・失敗時の安全経路を**ソース照合**。通信結果の検査は`SessionResult.ToJson`・`TipServer.Handle`・`TextService::Hello/HandleKey/Apply`の**該当メソッドを区切って実際の組立/読取文**まで確認する。Wire operation、主要フィールド、C#応答組立、ネイティブ読取の改変・Windows CI両トリガーからのNativeパス削除を**負例ミューテーション**で検知する。現行実装のliteral source contractであり、追加・削除された新フィールドや実際のパーサー挙動を網羅しない。
3. **T2の独立識別子設計**：Meltia専用のCLSID/GUID/プロファイル/管轄レジストリ、Meltia専用Pipe名、配布先とログ先を定義。元のMeltype資産を削除・登録せず、別モジュール・別PRで契約の変更内容を示す。
4. **T3の隔離TIP実装**：C#↔Nativeの双方で**同じ新しい名前とバージョン付きプロトコル**を使い、未知バージョン/未知操作/サイズ超過/接続失敗をfail-closed。開発中のKeyboard Previewは従来どおり非TSF。
5. **T4以降のWindows実機E2E**：文字消失/重複・前文字削除・候補・低/高権限・パスワード・異なるアプリ/32bit・繰り返し登録/解除を確認。実機の成功が出るまで独立IME配布を行わない。

## 6. テストの位置づけ

```bash
python3 tools/test_meltia_tip_ipc_contract.py
dotnet run --project src/Meltype.Core.Tests -c Release
```

前者はOSを問わない**ソース契約・ミューテーションテスト**であり、C++ DLLとC# Pipe間の実通信を開始しない。後者は継承Coreの動作テスト。これらに加えて、別途Windowsで実通信とインストール/アンインストールを行うE2Eが必要。入力本文をログ／資料に無断保存しない。

### レビュー指摘対応（2026-10-17）

- [PR #55](https://github.com/Fugu0141/Meltia/pull/55) P1：`meltia-windows-preview-ci.yml`の`pull_request.paths`と`push.paths`**双方**に`native/tip/**`を追加。Nativeコード単独の変更でも本IPCソース契約ガードが起動する。負例テストで双方の欠落を検証する。
- P2：以前の`field in server`／ファイル全体の`L"active"`存在だけの検査を撤廃。C#側では`SessionResult.ToJson`と`TipServer.Handle`の応答組立を、Native側では`TextService::HandleKey`の`active/consumed`、`TextService::Apply`の`commits/view/active`、`Hello`の`active`参照をメソッド限定で調べる。それぞれを改変したミューテーションで失敗を確認する。
- ただし静的ソース検査はC#／C++の構文・実行経路を解析するものではなく、**本格的なネイティブコンパイル、named-pipe実通信、TSF E2Eの代わりにはならない**。構造変更がある場合はテスト契約もレビューする。

**本PRはプロトコル内容の承認用。MeltiaとしてのTIPは一切登録しない。**
