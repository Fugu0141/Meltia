// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Fugu0141
import test from 'node:test';
import assert from 'node:assert/strict';
import { copyrightOf, spdxOf, implementationLines, movedCopyrightIsProven, sourcePathsFor } from './notices-provenance.mjs';

const name = 'src/CompositionController.cs';
const fresh = 'src/CompositionController.Conversion.cs';
const copyright = 'Copyright (C) 2026 Yukishiro';
const block = Array.from({length: 20}, (_, i) => `    var result${i} = Calculate(${i});`);
const source = ['// SPDX-License-Identifier: GPL-3.0-or-later', `// ${copyright}`, 'public class CompositionController', '{', ...block, '}'].join('\n');
const moved = ['// SPDX-License-Identifier: GPL-3.0-or-later', `// ${copyright}`, 'public partial class CompositionController', '{', ...block, '}'];
const target = {filename:fresh,status:'added',patch:moved.map(x=>`+${x}`).join('\n')};
const removed = {filename:name,status:'modified',patch:block.map(x=>`-${x}`).join('\n')};
const base = new Map([[name,source]]);
const isMoved = (newTarget=target, changes=[newTarget,removed], origin=base) =>
  movedCopyrightIsProven(newTarget,changes,origin,newTarget.patch.split('\n').map(x=>x.slice(1)),copyright);

test('reproduction: exact partial extraction preserves original copyright', () => {
  assert.deepEqual(sourcePathsFor(target,[target,removed],moved),[name]);
  assert.equal(isMoved(),true);
});
test('regression: a new implementation with old copyright is rejected', () => {
  const ownCode=moved.slice(0,4).concat(Array.from({length:20},(_,i)=>`var z${i} = NewMethod(${i});`),['}']);
  const fake={...target,patch:ownCode.map(x=>'+'+x).join('\n')};
  assert.equal(isMoved(fake,[fake,removed]),false);
});
test('regression: mixing new code into an extraction is not exempt', () => {
  const mixed=moved.slice(0,-1).concat(['    NewOperation();','}']);
  const fake={...target,patch:mixed.map(x=>'+'+x).join('\n')};
  assert.equal(isMoved(fake,[fake,removed]),false);
});
test('regression: do not trust copied headers without matching base code', () => {
  assert.equal(isMoved(target,[target,removed],new Map([[name,source.replace(block[12],'var stolen = 1;')]])),false);
  assert.equal(isMoved(target,[target,removed],new Map([[name,source.replace(copyright,'Copyright (C) 2026 Somebody')]])),false);
  assert.equal(isMoved(target,[target,removed],new Map()),false);
});
test('regression: no deleted code means no provenance', () => {
  assert.equal(isMoved(target,[target]),false);
  assert.equal(isMoved(target,[target,{...removed,patch:'-// Copyright (C) 2026 Yukishiro'}]),false);
  assert.equal(isMoved(target,[target,{...removed,patch:null}]),false);
});
test('regression: shorter or scattered coincidental snippets do not establish provenance', () => {
  const sample=moved.slice(0,4).concat(block.slice(0,6),['}']);
  const fake={...target,patch:sample.map(x=>'+'+x).join('\n')};
  assert.equal(isMoved(fake,[fake,removed]),false);
});
test('regression: mixed changes must not match a different old file name', () => {
  const unrelated={...removed,filename:'not-code.md'};
  assert.deepEqual(sourcePathsFor(target,[target,unrelated],moved),[]);
  assert.equal(isMoved(target,[target,unrelated],new Map([['not-code.md',source]])),false);
});
test('normalization discards wrappers, not implementation', () => {
  assert.deepEqual(implementationLines(['// Copyright (C) 2026 Yukishiro','namespace A;', 'public sealed partial class B', '{','foo();','}']),['foo();']);
});

test('review regression: literal copyright words in source code are not notices', () => {
  const valid = [
    '// Copyright (C) 2026 Yukishiro',
    '/// Copyright (C) 2026 Yukishiro',
    '# Copyright (C) 2026 Yukishiro',
    '; Copyright (C) 2026 Yukishiro',
    '/* Copyright (C) 2026 Yukishiro */',
    ' * Copyright (C) 2026 Yukishiro',
    '<# Copyright (C) 2026 Yukishiro #>',
  ];
  for (const item of valid) assert.equal(copyrightOf(item),'Copyright (C) 2026 Yukishiro');
  const literals = [
    "const value = '// Copyright (C) 2026 Yukishiro';",
    'assert.equal(name, "Copyright (C) 2026 Yukishiro")',
    'if (text.includes("Copyright (C) 2026 Yukishiro")) return;',
    'var code = "// Copyright (C) 2026 Yukishiro"; // text, not notice',
    'Copyright (C) 2026 Yukishiro',
  ];
  for (const item of literals) assert.equal(copyrightOf(item),null);
});
test('review regression: renamed source with deleted blocks is recognized by base path', () => {
  const renamed = {...removed,status:'renamed',filename:'src/RenamedController.cs',previous_filename:name};
  assert.deepEqual(sourcePathsFor(target,[target,renamed],moved),[name]);
  assert.equal(isMoved(target,[target,renamed]),true);
});
test('review regression: base source code literal is not a legitimate copyright header', () => {
  const fakeSource = ['const notAHeader = "Copyright (C) 2026 Yukishiro";',...block].join('\n');
  assert.equal(isMoved(target,[target,removed],new Map([[name,fakeSource]])),false);
});

test('review follow-up: SPDX text in code must not masquerade as a license header', () => {
  const valid = [
    '// SPDX-License-Identifier: GPL-3.0-or-later',
    '# SPDX-License-Identifier: GPL-3.0-or-later',
    '/* SPDX-License-Identifier: GPL-3.0-or-later */',
    ' * SPDX-License-Identifier: GPL-3.0-or-later',
    '<# SPDX-License-Identifier: GPL-3.0-or-later #>',
  ];
  for (const line of valid) {
    assert.equal(spdxOf(line), 'SPDX-License-Identifier: GPL-3.0-or-later');
  }
  for (const line of [
    'const info = "SPDX-License-Identifier: GPL-3.0-or-later";',
    'assert.match(value, /SPDX-License-Identifier/);',
    'SPDX-License-Identifier: GPL-3.0-or-later',
    '// SPDX-License-Identifier:',
    '// unrelated SPDX-License-Identifier: GPL-3.0-or-later',
  ]) assert.equal(spdxOf(line), null);
});
