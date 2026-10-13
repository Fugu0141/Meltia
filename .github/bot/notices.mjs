// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// ソースファイルの先頭のライセンス・著作権の表記を確かめる bot (.github/workflows/notices.yml から呼ぶ)。
//   - Pull Request で新しく足したソースファイルに `SPDX-License-Identifier` と `Copyright (C) <年> <名前>` があるか
//   - 作者 (yksr-melt) 以外の人が、Yukishiro (雪代) の名義で著作権の行を書いていないか
//     (貢献の著作権は貢献した人に残る: CONTRIBUTING.md の CLA。自分の名前で書いてもらう)
// 見つけたら Pull Request にコメントする (直ったらコメントを「直りました」に書き換える)。
// Pull Request のコードは動かさない。GitHub の API で、変更 (patch) の足された行を文字列として読むだけ。
import fs from 'node:fs';
import { movedCopyrightIsProven, sourcePathsFor } from './notices-provenance.mjs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

const Marker = '<!-- meltype-bot --><!-- notices -->';
/** Yukishiro 名義で書いてよい人 (作者)。 */
const Owners = ['yksr-melt'];
/** 先頭に表記を書くソースファイル。辞書・文書・設定ファイル・画像は見ない。 */
const SourceFile = /\.(cs|swift|ps1|psm1|sh|py|cpp|c|h|hpp|m|mm|mjs|js|ts|lisp|iss)$/i;
/** Yukishiro の名義 */
const OwnerName = /yukishiro|雪代|ゆきしろ|yksr/i;
/** 先頭の何行の中に表記があればよいか (shebang やエンコーディングの行の後に書くこともあるので少し余裕を持たせる) */
const HeaderLines = 8;

async function gh(method, url, body) {
  const response = await fetch(url.startsWith('http') ? url : `${api}/repos/${repo}${url}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

/** Pull Request で変わったファイル (patch つき)。 */
async function changedFiles(number) {
  const files = [];
  for (let page = 1; page <= 30; page++) {
    const batch = await gh('GET', `/pulls/${number}/files?per_page=100&page=${page}`) ?? [];
    files.push(...batch);
    if (batch.length < 100) break;
  }
  return files;
}

/** patch の足された行 (先頭の + を外す)。 */
const addedLines = patch => (patch ?? '').split('\n').filter(l => l.startsWith('+') && !l.startsWith('+++')).map(l => l.slice(1));

const copyrightOf = line => /copyright\s*(\(c\)|©)?\s*\d{4}/i.test(line) ? line.replace(/^[\s#/;*!<>-]*(rem\s+)?/i, '').trim() : null;

function check(files, author, baseFiles = new Map()) {
  const findings = [];
  const isOwner = Owners.some(o => o.toLowerCase() === author.toLowerCase());
  for (const file of files) {
    if (!SourceFile.test(file.filename)) continue;
    const added = addedLines(file.patch);
    if (file.status === 'added') {
      // patch が大きすぎると GitHub は patch を返さない。そのときは見ない
      if (!file.patch) continue;
      const head = added.slice(0, HeaderLines);
      const problems = [];
      if (!head.some(l => l.includes('SPDX-License-Identifier'))) problems.push('`SPDX-License-Identifier: GPL-3.0-or-later` がありません');
      const copyrights = head.map(copyrightOf).filter(Boolean);
      if (!copyrights.length) problems.push('`Copyright (C) <年> <あなたの名前>` がありません');
      if (!isOwner) for (const copyright of copyrights.filter(c => OwnerName.test(c))) {
        if (!movedCopyrightIsProven(file, files, baseFiles, added, copyright)) {
          problems.push(`原著作者名義の著作権表示 (\`${copyright}\`) について、base 上の既存コードからの移動を確認できませんでした。原著コードの表示は消さず、新規実装には自身の著作権表示を付けてください`);
        }
      }
      if (problems.length) findings.push({ file: file.filename, problems });
    }
    else if (!isOwner) {
      // 既存のファイルに、作者の名義の著作権の行を足していないか
      const copyright = added.map(copyrightOf).find(c => c && OwnerName.test(c));
      if (copyright && !movedCopyrightIsProven(file, files, baseFiles, added, copyright)) findings.push({ file: file.filename, problems: [`作者名義の著作権の行 (\`${copyright}\`) を追加しています。既存コードの移動を確認できない場合、新規実装にはあなたの名前を付けてください`] });
    }
  }
  return findings;
}

function message(findings, author) {
  const list = findings.map(f => `- \`${f.file}\`\n${f.problems.map(p => `  - ${p}`).join('\n')}`).join('\n');
  return [
    `@${author} さん、ソースファイルの先頭のライセンス・著作権の表記を確認させてください。`,
    '',
    list,
    '',
    '新しく作ったソースファイルの先頭には、次の 2 行を入れてください (コメントの記号はファイルに合わせてください)。',
    '貢献した部分の著作権は原則として貢献者に残ります。Meltia 独自の CLA は導入していません。名前は **あなたの名前** (GitHub のユーザー名など) にしてください。詳細は [Meltiaの貢献ガイド](https://github.com/Fugu0141/Meltia/blob/main/CONTRIBUTING.md) を参照してください。',
    '',
    '```',
    '// SPDX-License-Identifier: GPL-3.0-or-later',
    '// Copyright (C) 2026 あなたの名前',
    '```',
    '',
    '直して push すると、このコメントは自動で書き換わります。',
  ].join('\n');
}

async function upsert(number, body) {
  const comments = await gh('GET', `/issues/${number}/comments?per_page=100`) ?? [];
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (!body) {
    // 直った: 前に指摘していたら、直ったことを書く (指摘が無かったなら何もしない)
    if (mine && !mine.body.includes('✅')) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n✅ ライセンス・著作権の表記を確認しました。ありがとうございます。` });
    return;
  }
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n${body}` });
  else await gh('POST', `/issues/${number}/comments`, { body: `${Marker}\n${body}` });
}

/**
 * Provenance is read from the immutable PR base SHA, never from the PR head.
 * Limit API lookups; missing/oversized patches or sources cannot grant an exemption.
 */
async function loadBaseSources(files, author, baseSha) {
  const sources = new Map();
  if (Owners.some(o => o.toLowerCase() === author.toLowerCase())) return sources;
  if (!/^[a-f0-9]{40}$/i.test(baseSha ?? '')) return sources;
  const paths = new Set();
  for (const file of files) {
    if (!SourceFile.test(file.filename) || !file.patch) continue;
    const added = addedLines(file.patch);
    const copyrightLines = (file.status === 'added' ? added.slice(0, HeaderLines) : added)
      .map(copyrightOf).filter(c => c && OwnerName.test(c));
    if (!copyrightLines.length) continue;
    for (const path of sourcePathsFor(file, files, added)) {
      if (paths.size >= 24) break;
      paths.add(path);
    }
  }
  for (const path of paths) {
    const encoded = path.split('/').map(encodeURIComponent).join('/');
    const file = await gh('GET', `/contents/${encoded}?ref=${baseSha}`);
    if (file?.type !== 'file' || file.encoding !== 'base64' ||
        !Number.isInteger(file.size) || file.size > 1_000_000 ||
        typeof file.content !== 'string') continue;
    sources.set(path, Buffer.from(file.content, 'base64').toString('utf8'));
  }
  return sources;
}

async function main() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const pr = event.pull_request;
  const author = pr.user.login;
  const files = await changedFiles(pr.number);
  const baseSources = await loadBaseSources(files, author, pr.base?.sha);
  const findings = check(files, author, baseSources);
  for (const f of findings) console.log(`${f.file}: ${f.problems.join(' / ')}`);
  await upsert(pr.number, findings.length ? message(findings, author) : null);
}

/** node .github/bot/notices.mjs --self-test: 判定だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  const file = (filename, status, lines) => ({ filename, status, patch: lines.map(l => `+${l}`).join('\n') });
  const cases = [
    ['他の人が Yukishiro 名義', [file('a.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 Yukishiro'])], 'someone', 1],
    ['他の人が自分の名前', [file('a.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 someone'])], 'someone', 0],
    ['作者が Yukishiro 名義', [file('a.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 Yukishiro'])], 'yksr-melt', 0],
    ['表記が無い', [file('b.sh', 'added', ['#!/bin/bash', 'echo hi'])], 'yksr-melt', 1],
    ['shebang の後に表記', [file('b.sh', 'added', ['#!/bin/bash', '# SPDX-License-Identifier: GPL-3.0-or-later', '# Copyright (C) 2026 someone'])], 'someone', 0],
    ['ソースでないファイル', [file('docs/a.md', 'added', ['# title'])], 'someone', 0],
    ['既存のファイルに Yukishiro 名義を足した', [file('c.ps1', 'modified', ['# Copyright (C) 2026 Yukishiro'])], 'someone', 1],
    ['雪代 の名義', [file('d.swift', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 雪代'])], 'someone', 1],
    ['SPDX のみ不足', [file('e.cs', 'added', ['// Copyright (C) 2026 someone', 'class X {}'])], 'someone', 1],
    ['Copyright のみ不足', [file('e.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', 'class X {}'])], 'someone', 1],
    ['SPDX と Copyright が両方不足', [file('e.cs', 'added', ['class X {}'])], 'someone', 1],
    ['自己名義と原著名義の混在', [file('e.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 someone', '// Copyright (C) 2026 Yukishiro','class X {}'])], 'someone', 1],
  ];
  // Reproduction: a partial split moves exact method bodies out of the base file.
  const original = Array.from({ length: 20 }, (_, i) => `    var v${i} = Convert(${i});`);
  const source = '// SPDX-License-Identifier: GPL-3.0-or-later\\n// Copyright (C) 2026 Yukishiro\\n' + original.join('\\n');
  const moved = ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 Yukishiro', 'public sealed partial class Split', '{', ...original, '}'];
  const fresh = file('Split.cs', 'added', moved);
  const deleted = { filename: 'Original.cs', status: 'modified', patch: original.map(l => '-'+l).join('\\n') };
  const inherited = new Map([['Original.cs', source]]);
  const provenanceCases = [
    ['移動元が一致する partial 分割', [fresh, deleted], inherited, 0],
    ['移動元を確認できない', [fresh, deleted], new Map(), 1],
    ['新規実装と原著コードの混在', [file('Split.cs', 'added', [...moved.slice(0,-1), 'NewMethod();', '}']), deleted], inherited, 1],
    ['移動元のコードが異なる', [fresh, deleted], new Map([['Original.cs', source.replace('Convert(7)', 'Other(7)')]]), 1],
  ];
  let failed = 0;
  for (const [name, files, author, expected] of cases) {
    const actual = check(files, author).length;
    const ok = actual === expected;
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name} (指摘 ${actual} 件、期待 ${expected} 件)`);
  }
  for (const [name, files, sources, expected] of provenanceCases) {
    const actual = check(files, 'someone', sources).length;
    const ok = actual === expected;
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name} (指摘 ${actual} 件、期待 ${expected} 件)`);
  }
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
