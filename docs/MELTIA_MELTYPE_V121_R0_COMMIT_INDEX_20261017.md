# R0補助台帳 — Meltype分岐起点〜v1.2.1の145コミット全件（2026-10-17）

> [ファイル別78件の棚卸し](MELTIA_MELTYPE_V121_R0_FILE_INVENTORY_20261017.md) と併用する、上流コミットの**履歴・参照番号索引**。GitHub API compareの固定範囲 `cc456a00521ae6143680196adf0f0c4b96d131ae` → `47ace1428e5d4c9213f64dbbaf00d73a01386c69`を `per_page=20` で全8ページ取得している。**コミットごとの「受け入れ可」判定ではない。**

## 1. 全体

- 固定した参照：[Meltype B→v1.2.1 比較](https://github.com/yksr-melt/Meltype/compare/cc456a00521ae6143680196adf0f0c4b96d131ae...47ace1428e5d4c9213f64dbbaf00d73a01386c69)。
- GitHub compareが報告する**145コミットすべてを下に列挙**。マージコミット、トピックコミット、単純な`Merge origin/main`を含む。**145の独立した変更案や145件の未移植PRという意味ではない**。内容が重複するコミットは、変更の差分/親を確認してまとめて追跡する。
- メッセージから抽出した`#番号`は**PRかIssueかを区別しない未検証の参照ID**。移植判断では必ず実際のPR本文・差分・依存関係・Merged状態を確認する。
- 分類は先頭の`fix:`等だけに基づく**仮ラベル**。元コミットの本文から機能の安全性を判断しない。

| 仮ラベル | コミット数 |
| --- | ---: |
| 機能追加 | 16 |
| 修正 | 39 |
| OS別 | 2 |
| 統合/履歴 | 79 |
| 辞書 | 2 |
| テスト | 2 |
| 未分類 | 3 |
| 保守 | 2 |
| **合計** | **145** |

## 2. 次に読解すべき代表的な変更群（採用決定ではない）

| 領域 | 参照する上流の候補 | 適用前の注意 |
| --- | --- | --- |
| 確定・再送順の安全性 | `#258`、`#265`、`#355` | 原文、キーの重複・欠落、保護区間とMeltia独自BoundaryRefinementの両方を検証。最初に再現テストを作成 |
| 日本語・英語境界 | `#65`、`#375`、`#376`、`#377`、`#379`、`#351` | 既存Meltiaの研究Core/H11と機能重複がないか、Meltype側Detector/辞書レイヤーの差分を三方比較 |
| IME／TSF | `#60`、`#242`、`#366`、`#380`、`#410`、`#415` | 修正が現行TIPに必要でも本家GUID/パイプ/インストーラーを移植しない。独立設計とWindows実機テストが先 |
| 変換・候補・学習 | `#221`、`#277`、`#290`、`#284` | 状態・保存データの副作用がある。ユーザー辞書や履歴を破壊しないことを先に確認 |
| 機能追加・配布 | `#74`、`#293`、`#294`、`#367` | 機能は次期要件で採否決定、配布は本家リポジトリ固有設定を遮断 |

この表の#番号は**元メッセージで言及される識別子**であって、その番号を持つGitHub PRがすべて本範囲にマージされたと断言するものではない。コードの詳細・コミット親・重複は個別の採用シートへ記載する。

## 3. 全145コミットの履歴索引

| # | 上流commit | 仮ラベル | メッセージ内参照番号 | commitのタイトル |
| ---: | --- | --- | --- | --- |
| 1 | [`4b4e07eb87`](https://github.com/yksr-melt/Meltype/commit/4b4e07eb87924e950861b25ff76d8b0904cf2bc0) | 機能追加 | — | feat: 後ろが英語のバッククォートも半角にする (#243) |
| 2 | [`339cc81cc5`](https://github.com/yksr-melt/Meltype/commit/339cc81cc56b21e7b091bcdaa8348ea5a9088fb0) | 機能追加 | — | feat: F8 で半角カタカナにする (#74) |
| 3 | [`0f2fe58644`](https://github.com/yksr-melt/Meltype/commit/0f2fe586448714ec9dc7bc8e42029f7c68e3d59c) | 機能追加 | — | feat: 入力中の Ctrl+H を BackSpace にする (#150) |
| 4 | [`42262584b7`](https://github.com/yksr-melt/Meltype/commit/42262584b72103dc6d1cbbdb6c44fd82a5ccd7a8) | 修正 | — | fix: Meltype IME でも入力中の Ctrl+U/I/O/P でかな・英字を切り替える (#60) |
| 5 | [`4215d38c4e`](https://github.com/yksr-melt/Meltype/commit/4215d38c4e63d432fa59e20d6a2d581875f2fd31) | 修正 | — | fix: Meltype IME でも Shift+Space で全角スペースを入れる (#246, #297) |
| 6 | [`d09ec669d2`](https://github.com/yksr-melt/Meltype/commit/d09ec669d2dd1b329f1bb50e2822c8c2df78d896) | 修正 | — | fix: Meltype IME で /command + Space の後も変換できるようにする (#242) |
| 7 | [`81995bc3db`](https://github.com/yksr-melt/Meltype/commit/81995bc3db636bcf903aa56573b3d9781c96b501) | 修正 | — | fix: lo-fi・sci-fi を英語のままにする (#263) |
| 8 | [`7bc6472ad5`](https://github.com/yksr-melt/Meltype/commit/7bc6472ad5263667d8aac95a2bc302ea156768db) | 修正 | — | fix: anata を打っている途中で あnata にしない (#65) |
| 9 | [`3471994e82`](https://github.com/yksr-melt/Meltype/commit/3471994e821036105a1ee595a18800359b1be881) | 修正 | — | fix: アーティスト名 (Khalifa・Grande・B.o.B・Chri$tian など) を英字のままにする (#303) |
| 10 | [`5d83ae04c3`](https://github.com/yksr-melt/Meltype/commit/5d83ae04c3cc0dc706a2fb3cb374bd03d0e33fb6) | 修正 | — | fix: 英文の中の you を後ろの日本語に合わせて よう に確定し直さない (#265) |
| 11 | [`7ba38c8569`](https://github.com/yksr-melt/Meltype/commit/7ba38c856901ac6aa6742ee1f915cc8ef96605eb) | 修正 | — | fix: 確定し直しを、続けて確定する文字と 1 回で送る (#258) |
| 12 | [`2cb351535f`](https://github.com/yksr-melt/Meltype/commit/2cb351535f9c81f6f247c551e1654d0210a14863) | 修正 | — | fix: VS Code などのコードの行で、次に打つキーのモード (あ / A) を出す (#198) |
| 13 | [`ed8557ae20`](https://github.com/yksr-melt/Meltype/commit/ed8557ae20e4c500c9cd810813d8835437f7f5cf) | 修正 | — | fix: ping を英語にする・英単語の前後の空白のテストを足す (#248) |
| 14 | [`fa1fc992c6`](https://github.com/yksr-melt/Meltype/commit/fa1fc992c6a7dec0311451fa44b1bee48b2a9043) | 機能追加 | — | feat: 数字の後ろの かける・わる は × ÷ を最初の候補にする (#293) |
| 15 | [`e3c6e85aab`](https://github.com/yksr-melt/Meltype/commit/e3c6e85aabc1e54b88bbed27337e3debdfa43e16) | 機能追加 | — | feat: 打った日付・時刻に 10/9・3:15 の形の候補も出す (#294) |
| 16 | [`95ca0b7ed8`](https://github.com/yksr-melt/Meltype/commit/95ca0b7ed810a5bfc8ef8b4a43637a524adbe178) | 修正 | — | fix: Microsoft IME の変換エンジンに長い読みを区切って渡す (#221) |
| 17 | [`411d7f4fd1`](https://github.com/yksr-melt/Meltype/commit/411d7f4fd1c552ff359f5fd7ecbc24cd414d5f9c) | 修正 | — | fix: 日本語の後ろのキャメルケースの語で全体を英字にしない (kyouhaGitHubni → きょうはGitHubに) |
| 18 | [`398a1c4495`](https://github.com/yksr-melt/Meltype/commit/398a1c4495d7cdd0090f49f83722fb52bb1139b4) | 修正 | — | fix: アプリ別設定であとから足した Enabled: false を効かせる (#233) |
| 19 | [`b1c95287c2`](https://github.com/yksr-melt/Meltype/commit/b1c95287c2d1b67ad8d0ecf4d752f050d1a3467b) | 修正 | — | fix: クラス名が入力欄の Web の要素 (Chatwork の検索欄) を入力欄として扱う (#70) |
| 20 | [`547cacd949`](https://github.com/yksr-melt/Meltype/commit/547cacd9494e45f79002cc4bde21311cb5a6eae1) | 修正 | — | fix: コードのエディターで 半角/全角 で日本語にしたら、改行しても日本語のまま続ける (#214) |
| 21 | [`0cea06849f`](https://github.com/yksr-melt/Meltype/commit/0cea06849fea4c0a9ad951d2536a44cd230a2697) | 修正 | — | fix: かな入力で Shift が重なった 1 文字を英字のまま残さない (#135) |
| 22 | [`a7ceec186e`](https://github.com/yksr-melt/Meltype/commit/a7ceec186e4bf97f4b846ae4114edfa30ebf3fb0) | 機能追加 | — | feat: 設定「変換したあとに続けて打っても確定しない」を足す (#123) |
| 23 | [`2f069fbd1c`](https://github.com/yksr-melt/Meltype/commit/2f069fbd1c2328947d16ed46a0854272ad23eeb0) | 修正 | — | fix: ULE4JIS などキー配列を変えるソフトのキーを処理する設定を案内する (#250) |
| 24 | [`c3b6969b62`](https://github.com/yksr-melt/Meltype/commit/c3b6969b6289ab895fecaeb764f14bd0109ed369) | 機能追加 | — | feat: 最近の学習だけを消せるようにし、学習データの棚卸しを書く (#277) |
| 25 | [`c4958440cd`](https://github.com/yksr-melt/Meltype/commit/c4958440cdfc7e77be23a0c6374593306ebf6047) | 機能追加 | — | feat: ローマ字のまま入った文を、選んで変換キーで日本語にする (#288) |
| 26 | [`15f38f33d2`](https://github.com/yksr-melt/Meltype/commit/15f38f33d2bf2e5124a0953fda80b5b37d50631a) | 機能追加 | — | feat: ;名前 で、ユーザーが登録した定型文を出す (#290) |
| 27 | [`6f6b6d3101`](https://github.com/yksr-melt/Meltype/commit/6f6b6d31018ee725b328180aef1d1518061cf738) | 機能追加 | — | feat: 自分で直した誤判定を、その場で 1 件の報告にする (#284) |
| 28 | [`f8f948d0d7`](https://github.com/yksr-melt/Meltype/commit/f8f948d0d796af7c066ff7c5329372c975692ffc) | 機能追加 | — | feat: 入力中の Space・変換・無変換・かなキーの役割を分ける設定 (#199) |
| 29 | [`eba6549099`](https://github.com/yksr-melt/Meltype/commit/eba65490993d827784f8b6ebc9e8fa71d5e6be08) | 修正 | — | fix: avoid Tab conversion for protected spans |
| 30 | [`a1ef0f5497`](https://github.com/yksr-melt/Meltype/commit/a1ef0f54978109b5278d894f3fd67798a49112e4) | 機能追加 | — | feat: 更新の前に「あなたの打ち方で変わる語」を見せる (#285) |
| 31 | [`89169aa12a`](https://github.com/yksr-melt/Meltype/commit/89169aa12afb52a6146b45fc1b0c54520feb982d) | 修正 | — | fix: サインインしたらすぐ Meltype を起動する (タスク スケジューラ) (#283) |
| 32 | [`a63bd99c65`](https://github.com/yksr-melt/Meltype/commit/a63bd99c655f7661200e4b814eeb46737d0b0b64) | OS別 | — | fcitx5: 半角/全角 で切り替えた入力モードをカーソル位置に表示する (#360) |
| 33 | [`827b6ab4f9`](https://github.com/yksr-melt/Meltype/commit/827b6ab4f90fcc14727bb03df380c7bdb4861989) | OS別 | — | fcitx5: 5.1.16 以降のヘッダに合わせて C++20 でビルドする (#361) |
| 34 | [`d81ccbfd5a`](https://github.com/yksr-melt/Meltype/commit/d81ccbfd5a1a724b04046a2187128532c44b75b1) | 修正 | — | fix: Meltype IME でも、コードエディターのコードの行は英数で始める (#366) |
| 35 | [`2a811565f8`](https://github.com/yksr-melt/Meltype/commit/2a811565f8468659d3b58967f265b92303c3778c) | 修正 | — | fix: Meltype IME で、管理者のアプリで 半角/全角 を押すと @ が入る (#366) |
| 36 | [`26cb4b8071`](https://github.com/yksr-melt/Meltype/commit/26cb4b80710b209bd8b4af5798935d68fba4440f) | 統合/履歴 | — | Merge pull request #355 from yksr-melt/codex/fix-tab-mixed-protected-input |
| 37 | [`4be2f4408f`](https://github.com/yksr-melt/Meltype/commit/4be2f4408f9acd879b39ddd71d76dc0cc4a5ddb0) | 修正 | — | fix: cwd + で のように語の最後と助詞の頭が同じでも区切る (#375) |
| 38 | [`ddf008eae5`](https://github.com/yksr-melt/Meltype/commit/ddf008eae5adafacd91eb02efcda58ec669ac5d6) | 修正 | — | fix: huihui を英字のままにする (#369) |
| 39 | [`af15459bf3`](https://github.com/yksr-melt/Meltype/commit/af15459bf32ff622d1b8e9fcd31f7e97edf184ab) | 辞書 | — | dict: き の候補に 氣 (気 の旧字体) を足す (#371) |
| 40 | [`a1297b2708`](https://github.com/yksr-melt/Meltype/commit/a1297b2708759f6150a449ae030681a8e97f5634) | 修正 | — | fix: しゃーなし を 西なし にしない (#368) |
| 41 | [`dd5a8d82d4`](https://github.com/yksr-melt/Meltype/commit/dd5a8d82d4b9861291824898d2308aa7805f5c86) | 修正 | — | fix: ユーザー辞書の語を、変換エンジンの文節の途中には割り込ませない (#374) |
| 42 | [`b2d3d1063d`](https://github.com/yksr-melt/Meltype/commit/b2d3d1063d045d2784102704255cb2093e665f21) | 修正 | — | fix: Chrome リモート デスクトップ・Parsec で操作されている間も変換する (#372) |
| 43 | [`d4d7fe91ee`](https://github.com/yksr-melt/Meltype/commit/d4d7fe91ee85415c688346598e26ff72d50ffbc8) | 修正 | — | fix: LINE ではフォーカスがウィンドウにあっても入力欄として扱う (#373) |
| 44 | [`23859a10be`](https://github.com/yksr-melt/Meltype/commit/23859a10be8675066867bb2a7f5aae56e8836325) | 修正 | — | fix: ブラウザーで「入力欄ではない」のままにならないよう、打ったときに調べ直す (#364) |
| 45 | [`6da0ede589`](https://github.com/yksr-melt/Meltype/commit/6da0ede5891f57b2a277756b919a9c7382b81a99) | 機能追加 | — | feat: 入力モードの表示 (あ / A) の色を設定で変えられるようにする (#359) |
| 46 | [`f916f8f33f`](https://github.com/yksr-melt/Meltype/commit/f916f8f33f8f25a936831e0e8f8ef57a2251f481) | 機能追加 | — | feat: winget に出すマニフェストをインストーラーにし、リリースごとに自動で出す (#367) |
| 47 | [`35464e40ba`](https://github.com/yksr-melt/Meltype/commit/35464e40ba6c9174a34063e708a30c28a24c9288) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 48 | [`81ed518d1a`](https://github.com/yksr-melt/Meltype/commit/81ed518d1aeedc9b2f2bd6827d1dad32022febed) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 49 | [`12eb3932c2`](https://github.com/yksr-melt/Meltype/commit/12eb3932c2d7d65dbcae8ec6dbb9f92debd4018f) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 50 | [`fbccc84a4a`](https://github.com/yksr-melt/Meltype/commit/fbccc84a4a3dab058ebfd1197fa0a03d67ee4677) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 51 | [`dc0a534d54`](https://github.com/yksr-melt/Meltype/commit/dc0a534d54fd790826022ccc38b17aff25ee28ad) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 52 | [`8c41c702aa`](https://github.com/yksr-melt/Meltype/commit/8c41c702aa8c0a6291ec00452dc3e5ffc1f45182) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 53 | [`62c231c8f7`](https://github.com/yksr-melt/Meltype/commit/62c231c8f746e90447fe24d1b373c06c4e3bb51b) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 54 | [`674a139e42`](https://github.com/yksr-melt/Meltype/commit/674a139e42d4d71fba1d00f996f6c621a634b53f) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 55 | [`f1070154cc`](https://github.com/yksr-melt/Meltype/commit/f1070154ccc0aaa0acf65776b78298d8a9565227) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 56 | [`2726980574`](https://github.com/yksr-melt/Meltype/commit/2726980574bcda0068e7e0cf295d25c6f9976ee6) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 57 | [`f838ca3362`](https://github.com/yksr-melt/Meltype/commit/f838ca33626eead3e05a2179881e2c14ad4e983a) | 統合/履歴 | — | Merge origin/main (CI を直すため、Tab で変換 #299 の直しを取り込む) |
| 58 | [`2a2bf10723`](https://github.com/yksr-melt/Meltype/commit/2a2bf10723712d145abae7a64fb20beb97105670) | 修正 | — | fix: password + に + して を passwordにsite にしない (#376) |
| 59 | [`59649cee3d`](https://github.com/yksr-melt/Meltype/commit/59649cee3dc5f1957f92f6ae2461aa1bbe778484) | 修正 | — | fix: gameover のような英単語 2 つの合成語を英字のままにする (#377) |
| 60 | [`fe8af43383`](https://github.com/yksr-melt/Meltype/commit/fe8af433836478be3663c49ec714ab377fb0bdf7) | 修正 | — | fix: face + 助詞 を ふぁせ にしない (#379) |
| 61 | [`338cca9a6f`](https://github.com/yksr-melt/Meltype/commit/338cca9a6f5e63a655c6901abac9c3c1ca5beb6f) | 修正 | — | fix: やばいTシャツ屋さん の T の後ろを日本語にする (#351) |
| 62 | [`8d05923234`](https://github.com/yksr-melt/Meltype/commit/8d05923234faf552b76237b07197cb2d2488cd9e) | テスト | — | test: delegate が でlegate にならないことを確かめる (#357) |
| 63 | [`57f913794d`](https://github.com/yksr-melt/Meltype/commit/57f913794d113a36fae472378869fdd0903a136b) | テスト | — | test: memoirs が英字のままになることを確かめる (#378) |
| 64 | [`eebd14bb33`](https://github.com/yksr-melt/Meltype/commit/eebd14bb3303ad1cc233c713a295b40aea64e019) | 辞書 | — | dict: よく使う動詞の敬語 (尊敬語・謙譲語) を候補に出す (#352) |
| 65 | [`fcfde2b8ab`](https://github.com/yksr-melt/Meltype/commit/fcfde2b8abd103cb165cb745c19b7ca4e89f54c2) | 修正 | — | fix: 報告の再現のチェックで、報告の欄のバッククォートを外す |
| 66 | [`bcebbaf337`](https://github.com/yksr-melt/Meltype/commit/bcebbaf337eec7d19ee6c8409dfa10a6abde70d0) | 統合/履歴 | — | Merge #306 (feat/243-backtick-halfwidth) |
| 67 | [`85aa181742`](https://github.com/yksr-melt/Meltype/commit/85aa181742278c672460013e337057bc7d205f29) | 統合/履歴 | — | Merge #307 (feat/74-f8-halfwidth-katakana) |
| 68 | [`cfc7cca6e2`](https://github.com/yksr-melt/Meltype/commit/cfc7cca6e253103dd5ae8c670699350331552b16) | 統合/履歴 | — | Merge #308 (feat/150-ctrl-h-backspace) |
| 69 | [`14df280cc9`](https://github.com/yksr-melt/Meltype/commit/14df280cc90964f9921154278e978cdd39972f49) | 統合/履歴 | — | Merge #309 (fix/60-ctrl-uiop-ime) |
| 70 | [`c531b20a49`](https://github.com/yksr-melt/Meltype/commit/c531b20a494d9fef6097ff58eeaa59e3017cdd64) | 統合/履歴 | — | Merge #310 (fix/246-ime-shift-space) |
| 71 | [`b40aca0212`](https://github.com/yksr-melt/Meltype/commit/b40aca0212c9751ae6ab1f1ece917f354b91f580) | 統合/履歴 | — | Merge #311 (fix/242-sigil-stuck-ime) |
| 72 | [`9bebf1a03b`](https://github.com/yksr-melt/Meltype/commit/9bebf1a03b809d19538da24dc77e3cab0511b439) | 統合/履歴 | — | Merge #312 (fix/263-lo-fi) |
| 73 | [`cb33ece1f1`](https://github.com/yksr-melt/Meltype/commit/cb33ece1f18b38743f68b78a71f902a1626a4bf7) | 統合/履歴 | — | Merge #314 (fix/65-anata) |
| 74 | [`1322bb32b8`](https://github.com/yksr-melt/Meltype/commit/1322bb32b8301bf3dcad7f4da6f12fd82ecda989) | 統合/履歴 | — | Merge #315 (fix/303-artist-names) |
| 75 | [`c8cbe07a59`](https://github.com/yksr-melt/Meltype/commit/c8cbe07a59bcd5ae3779b1794df2c2eebae5f117) | 統合/履歴 | — | Merge #317 (fix/265-you-rewrite) |
| 76 | [`5785602f02`](https://github.com/yksr-melt/Meltype/commit/5785602f029c65a91d16575a895e6785e4e06bc2) | 統合/履歴 | — | Merge #318 (fix/258-rewrite-order) |
| 77 | [`c8bde2d50f`](https://github.com/yksr-melt/Meltype/commit/c8bde2d50f5e3539027e1796b13120b4a26d9dfe) | 統合/履歴 | — | Merge #320 (fix/248-ing-words) |
| 78 | [`69f8999e88`](https://github.com/yksr-melt/Meltype/commit/69f8999e88b886dc75e9fe453c486bf8477e7ea7) | 統合/履歴 | — | Merge #321 (feat/293-math-symbols) |
| 79 | [`d4f5230156`](https://github.com/yksr-melt/Meltype/commit/d4f523015651e275220da00a3f60bf2a88e94c08) | 統合/履歴 | — | Merge #322 (feat/294-date-candidates) |
| 80 | [`dfb845af95`](https://github.com/yksr-melt/Meltype/commit/dfb845af95d3c18509e72c54a2caec22bed45cfa) | 統合/履歴 | — | Merge #323 (fix/221-msime-long-input) |
| 81 | [`f99066f33c`](https://github.com/yksr-melt/Meltype/commit/f99066f33cb18cbb9448ea5529583ce4d1577551) | 統合/履歴 | — | Merge #324 (claude/epic-ellis-8a8ce8) |
| 82 | [`341db28aca`](https://github.com/yksr-melt/Meltype/commit/341db28aca024e202091e84769811b9d31e49d75) | 統合/履歴 | — | Merge #325 (fix/233-app-rule-disable) |
| 83 | [`1eab54945c`](https://github.com/yksr-melt/Meltype/commit/1eab54945c3e3472ac0c2c87220699951eb1bb45) | 統合/履歴 | — | Merge #326 (fix/70-chatwork-search) |
| 84 | [`2e3f2a08df`](https://github.com/yksr-melt/Meltype/commit/2e3f2a08df394d2b38d7618b9bb31433c657694a) | 統合/履歴 | — | Merge #327 (fix/214-code-line-japanese) |
| 85 | [`6ce00b1a3f`](https://github.com/yksr-melt/Meltype/commit/6ce00b1a3fb044670b991e9e2bd9c7159b0f9402) | 統合/履歴 | — | Merge #328 (fix/135-kana-shift) |
| 86 | [`c38ea8112b`](https://github.com/yksr-melt/Meltype/commit/c38ea8112b08185e2edfc77beeff3db2aa2dd9ca) | 統合/履歴 | — | Merge #329 (feat/123-continue-after-conversion) |
| 87 | [`42c78b39ad`](https://github.com/yksr-melt/Meltype/commit/42c78b39adcfb173016c0c234570b36f05ed0465) | 統合/履歴 | — | Merge #330 (fix/250-key-remapper-input) |
| 88 | [`09c616d6a7`](https://github.com/yksr-melt/Meltype/commit/09c616d6a7d939190602c3d487bf6e8d6dbff347) | 統合/履歴 | — | Merge #331 (feat/277-forget-learning) |
| 89 | [`7e0ed0959b`](https://github.com/yksr-melt/Meltype/commit/7e0ed0959b095cb9ff8326e08f4c04c13bfa90fa) | 統合/履歴 | — | Merge #332 (feat/288-romaji-reconversion) |
| 90 | [`6992c45fb4`](https://github.com/yksr-melt/Meltype/commit/6992c45fb48e09002c9e1f63941cf1cce6f142d2) | 統合/履歴 | — | Merge #334 (feat/290-snippets) |
| 91 | [`9438270db4`](https://github.com/yksr-melt/Meltype/commit/9438270db4659f1f0762da3435bc059827450089) | 統合/履歴 | — | Merge #335 (feat/284-report-correction) |
| 92 | [`7c545fcaba`](https://github.com/yksr-melt/Meltype/commit/7c545fcaba6f6ce20e2b85699c2c13532a400453) | 統合/履歴 | — | Merge #336 (feat/199-key-roles) |
| 93 | [`fc1ccb9d7b`](https://github.com/yksr-melt/Meltype/commit/fc1ccb9d7ba6a661c6090cfe3397555751bafaa9) | 統合/履歴 | — | Merge #356 (feat/285-update-preview) |
| 94 | [`9ba031f880`](https://github.com/yksr-melt/Meltype/commit/9ba031f880081d23250a39b042ce6234515954da) | 統合/履歴 | — | Merge #358 (fix/283-startup-task) |
| 95 | [`90ec19150d`](https://github.com/yksr-melt/Meltype/commit/90ec19150dba0758e15df0545559d693509fc6bd) | 統合/履歴 | — | Merge #381 (fix/375-cwd-de) |
| 96 | [`2f16ad2fbb`](https://github.com/yksr-melt/Meltype/commit/2f16ad2fbbf67b2d7cf6d03e675f7befa8ad77aa) | 統合/履歴 | — | Merge #382 (fix/369-huihui) |
| 97 | [`797801a26a`](https://github.com/yksr-melt/Meltype/commit/797801a26ab6c4cb38acf4d955d8b353353161ea) | 統合/履歴 | — | Merge #383 (dict/371-ki-kyujitai) |
| 98 | [`38f5380c48`](https://github.com/yksr-melt/Meltype/commit/38f5380c482ad5f487aa7d9ff2a5e76477bbfa2a) | 統合/履歴 | — | Merge #384 (fix/368-shanashi) |
| 99 | [`575e51cb76`](https://github.com/yksr-melt/Meltype/commit/575e51cb7635734d01c4938b6e32e72f1fd82eb9) | 統合/履歴 | — | Merge #385 (fix/374-userdict-split) |
| 100 | [`1f2f705789`](https://github.com/yksr-melt/Meltype/commit/1f2f7057893230e838925dc34d3b968795b9ed59) | 統合/履歴 | — | Merge #386 (fix/372-remote-control-input) |
| 101 | [`8852326077`](https://github.com/yksr-melt/Meltype/commit/8852326077b3b13819c1e3de71d6b309a6c71ba1) | 統合/履歴 | — | Merge #387 (fix/373-line-focus) |
| 102 | [`481b6a266f`](https://github.com/yksr-melt/Meltype/commit/481b6a266f8760f0d2f50cdf8e608c9d1b926eff) | 統合/履歴 | — | Merge #388 (fix/364-firefox-stale-focus) |
| 103 | [`020de805e6`](https://github.com/yksr-melt/Meltype/commit/020de805e6609ca339f0ff965e1c7c82f23a5da8) | 統合/履歴 | — | Merge #389 (feat/359-indicator-colors) |
| 104 | [`41c9d85c57`](https://github.com/yksr-melt/Meltype/commit/41c9d85c5706318a29509486f10f964f1fae82cf) | 統合/履歴 | — | Merge #390 (feat/367-winget) |
| 105 | [`4503b7d475`](https://github.com/yksr-melt/Meltype/commit/4503b7d47552597545c5274713a0bfe9b16c60b9) | 統合/履歴 | — | Merge #392 (fix/376-particle-suru) |
| 106 | [`3dc5b2a74a`](https://github.com/yksr-melt/Meltype/commit/3dc5b2a74a50bf2f55e1ae7abbc6c36cfad1d082) | 統合/履歴 | — | Merge #394 (fix/377-english-compound) |
| 107 | [`deb4561974`](https://github.com/yksr-melt/Meltype/commit/deb4561974da8de624825bbd9fb3fe3649c970ad) | 統合/履歴 | — | Merge #395 (fix/379-face-particle) |
| 108 | [`4affbad389`](https://github.com/yksr-melt/Meltype/commit/4affbad3891bbc6bc9ad30f092c53a213cd5e7e8) | 統合/履歴 | — | Merge #396 (fix/351-t-shirt) |
| 109 | [`2e15b168de`](https://github.com/yksr-melt/Meltype/commit/2e15b168deddf3159d0719df8bb895d04a60f4cb) | 統合/履歴 | — | Merge #398 (test/357-delegate) |
| 110 | [`f0a15a06ca`](https://github.com/yksr-melt/Meltype/commit/f0a15a06ca701ca39f6d2afd6c8465a482dfb7fa) | 統合/履歴 | — | Merge #399 (test/378-memoirs) |
| 111 | [`3561944280`](https://github.com/yksr-melt/Meltype/commit/35619442808ea1bee66e9188acc284db61ff3b0a) | 統合/履歴 | — | Merge #400 (dict/352-keigo) |
| 112 | [`88d4880691`](https://github.com/yksr-melt/Meltype/commit/88d48806912132ac2c465b325b6ad5c958df6945) | 統合/履歴 | — | Merge #401 (fix/pr-checks-issue-backticks) |
| 113 | [`5102ac8f52`](https://github.com/yksr-melt/Meltype/commit/5102ac8f528226ae4d9ab8975dd446efac305736) | 未分類 | — | 統合ブランチ: 衝突の解き方で崩れたテストのメソッドと、設定の空行を直す |
| 114 | [`7596007b9c`](https://github.com/yksr-melt/Meltype/commit/7596007b9c77f0295b098af7a68f6aae7b71329d) | 修正 | — | fix: support text input in Affinity without app registration |
| 115 | [`6f3b5fc3ca`](https://github.com/yksr-melt/Meltype/commit/6f3b5fc3ca351bcbb9e52e9114d22eacc4287d3d) | 修正 | — | fix: use a high-resolution clock for Affinity query deadlines |
| 116 | [`45b9c7a35a`](https://github.com/yksr-melt/Meltype/commit/45b9c7a35a15c99fcb431c02164645c2a9d30a85) | 統合/履歴 | — | Merge pull request #403 from hirokawa-beach/fix/issue-289-affinity-text-input |
| 117 | [`a999076326`](https://github.com/yksr-melt/Meltype/commit/a999076326f16e3a4c58a81eb096130ca7136c3f) | 統合/履歴 | — | Merge pull request #362 from azzz9/feat/360-mode-indicator |
| 118 | [`a468a6b3f6`](https://github.com/yksr-melt/Meltype/commit/a468a6b3f6339c0adec089fa597775b2a20ec0ae) | 統合/履歴 | — | Merge pull request #380 from asutarisucu/fix/366-ime-hankaku-elevated |
| 119 | [`81b78bf84c`](https://github.com/yksr-melt/Meltype/commit/81b78bf84c84b3195d176630f1ddbe2b19a30f6f) | 統合/履歴 | — | Merge pull request #363 from azzz9/fix/361-cxx20 |
| 120 | [`29ff074bd6`](https://github.com/yksr-melt/Meltype/commit/29ff074bd66c4269cf153939bef607a4481349a7) | 修正 | — | fix: キーの直後のクリックでも、コードの行を英数に戻す (#366) |
| 121 | [`18defd7fb3`](https://github.com/yksr-melt/Meltype/commit/18defd7fb342068866244c545f3ed04203565f97) | 統合/履歴 | — | Merge main into integration/v1.2.0 and resolve conflicts |
| 122 | [`4b2008c8f3`](https://github.com/yksr-melt/Meltype/commit/4b2008c8f3c81345d446ed2804d0fd15123ef421) | 統合/履歴 | — | Merge pull request #370 from asutarisucu/fix/366-ime-code-editor |
| 123 | [`c45677a907`](https://github.com/yksr-melt/Meltype/commit/c45677a9074e9f2703d90865454eb05a794d0edd) | 保守 | — | chore: bump version to 1.2.0 |
| 124 | [`b355aaa53e`](https://github.com/yksr-melt/Meltype/commit/b355aaa53e6f8bee14c658bfa08e43b02dd796b9) | 統合/履歴 | — | Merge remote-tracking branch 'origin/main' into fix/198-vscode-mode-indicator |
| 125 | [`c8830625af`](https://github.com/yksr-melt/Meltype/commit/c8830625af7818278bba0caa7f8d4125d9a0217f) | 未分類 | — | 定型文: 入力欄にフォーカスがないときは展開しない |
| 126 | [`2f59cf5fe4`](https://github.com/yksr-melt/Meltype/commit/2f59cf5fe409b52771d2ad64ad066b4575d0098a) | 未分類 | — | 遠隔操作: Parsec が動いている間はずっと処理する、という既知の問題を設定の説明に書く |
| 127 | [`f116f42c92`](https://github.com/yksr-melt/Meltype/commit/f116f42c927245c9cb0f53e607263731445002ac) | 統合/履歴 | — | Merge origin/main (衝突の解消) と、「Windows の起動時に起動」にタスク スケジューラに登録することを説明に出す |
| 128 | [`7d2e053f7d`](https://github.com/yksr-melt/Meltype/commit/7d2e053f7db6c96d79693111dbbbfda060b971b7) | 統合/履歴 | — | Merge feat/290-snippets (レビューの直し) |
| 129 | [`4fe70dbc48`](https://github.com/yksr-melt/Meltype/commit/4fe70dbc48005ffd892a7202123be49a6d36f0a9) | 統合/履歴 | — | Merge fix/372-remote-control-input (レビューの直し) |
| 130 | [`ea94224854`](https://github.com/yksr-melt/Meltype/commit/ea9422485466ab05bc6d5482be6783d23a3ea0c5) | 統合/履歴 | — | Merge fix/283-startup-task (レビューの直しと main の取り込み) |
| 131 | [`639e395697`](https://github.com/yksr-melt/Meltype/commit/639e395697da01fb2ddbe48f3b801f9727441359) | 統合/履歴 | — | Merge pull request #319 from asutarisucu/fix/198-vscode-mode-indicator |
| 132 | [`e49d8773e6`](https://github.com/yksr-melt/Meltype/commit/e49d8773e675bccddd49fc241cc925fec3790895) | 統合/履歴 | — | Merge origin/integration/v1.2.0 (main の取り込みと版の更新) |
| 133 | [`81f63758d7`](https://github.com/yksr-melt/Meltype/commit/81f63758d700aa16c9eda037bb90bc059438c0af) | 統合/履歴 | — | Merge main (#319: コードの行のモード表示) |
| 134 | [`afa8324f63`](https://github.com/yksr-melt/Meltype/commit/afa8324f637d92d254fb854442c302902614d267) | 修正 | — | fix: 古い Meltype IME の DLL を読み込んだままのアプリでは、コードの入力欄として扱わない (#366) |
| 135 | [`36554ae47b`](https://github.com/yksr-melt/Meltype/commit/36554ae47b868f7cf24f4fe03c23736b2cff62fd) | 統合/履歴 | — | Merge pull request #402 from yksr-melt/integration/v1.2.0 |
| 136 | [`e4b0a8c957`](https://github.com/yksr-melt/Meltype/commit/e4b0a8c957b61dad0dc9aec45556a566ce18dd32) | 統合/履歴 | — | Merge pull request #410 from asutarisucu/fix/366-ime-old-dll |
| 137 | [`87296af353`](https://github.com/yksr-melt/Meltype/commit/87296af353f6991eb5aa323bb1e48a50cf0ad05e) | 機能追加 | — | feat: Meltype IME を既定の動作モードにする (#366) |
| 138 | [`80e7f322d5`](https://github.com/yksr-melt/Meltype/commit/80e7f322d5bd888fc08102785d5f0a7d745918e3) | 修正 | — | fix: ARM64 の Windows では、起動したときに Meltype キーボードに戻す (#366) |
| 139 | [`d7d56343e4`](https://github.com/yksr-melt/Meltype/commit/d7d56343e4b153231f7493ae7b45ec20cd351bfb) | 統合/履歴 | — | Merge pull request #415 from asutarisucu/feat/366-ime-default |
| 140 | [`6dc58a7d65`](https://github.com/yksr-melt/Meltype/commit/6dc58a7d65b9d6964a51130fb5cfabc0275d1698) | 機能追加 | — | feat: 英字の後ろの , . も句読点にする設定を足す (#416) |
| 141 | [`75d625d444`](https://github.com/yksr-melt/Meltype/commit/75d625d4440083559d0067fc46397d21691e1764) | 修正 | — | fix: Main に [STAThread] を付け直す (v1.2.0 の自己診断でクリップボードが失敗していた) |
| 142 | [`addfe40059`](https://github.com/yksr-melt/Meltype/commit/addfe4005953513eaafa9ec9830d7bdc762dabcb) | 統合/履歴 | — | Merge pull request #423 from yksr-melt/fix/main-stathread |
| 143 | [`1ee659a0f7`](https://github.com/yksr-melt/Meltype/commit/1ee659a0f7eb7801b774f8330086892c1d12864f) | 統合/履歴 | — | Merge pull request #418 from yksr-melt/feat/416-punctuation-after-english |
| 144 | [`86ba66b196`](https://github.com/yksr-melt/Meltype/commit/86ba66b196258a4f6f6df1b58db00e4830ddda56) | 保守 | — | chore: 版を 1.2.1 にする (Windows・Mac・Linux) |
| 145 | [`47ace1428e`](https://github.com/yksr-melt/Meltype/commit/47ace1428e5d4c9213f64dbbaf00d73a01386c69) | 統合/履歴 | — | Merge pull request #425 from yksr-melt/chore/bump-1.2.1 |

## 4. 次の受け入れ判断の作業

1. まず`test:`コミットと上流v1.2.1テストの内容を現行Meltia契約と照合し、**再現可能なテストケース**を移植する。
2. `fix:`コミットはP0/P1相当の入力安全性から1件ずつ、差分と依存コミット・Meltia側の局所変更を解析。既に解決している場合は`covered`。
3. `feat:`、`dict:`、OS／配布は今すぐ一括取り込まない。要件定義とライセンス・データ再配布条件を確認する。
4. 145コミットのうち**マージと履歴統合を含むものを二重に実装しない**。このファイルは「探索の索引」で、採用結果は[受け入れ判定シート](MELTIA_UPSTREAM_INTAKE_TEMPLATE.md)を複製して残す。

この文書を追加してもMeltiaの実装・コードの追従率・バージョンは変わらない。
