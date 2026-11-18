#!/usr/bin/env node
// Copies the MatGame JS SDK (sdk/js) into a game: node ../MatGame/tools/sdk-copy.js <target folder>
//   BunnyWarfare: node ../MatGame/tools/sdk-copy.js src/BunnyWarfare.Module/wwwroot/js/matgame
//   KingBee     : node ../MatGame/tools/sdk-copy.js src/matgame
// Every copied file gets a first line naming the version; matgame.version lists the version and the SHA-256 of every file (a test of the game holds the copy to it).
// Never edit the copy: change sdk/js here and copy again.

import { createHash } from 'node:crypto';
import { mkdirSync, readFileSync, readdirSync, writeFileSync, rmSync, existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const target = process.argv[2];
if (!target) {
    console.error('usage: node tools/sdk-copy.js <target folder>');
    process.exit(1);
}

const props = readFileSync(join(root, 'Directory.Build.props'), 'utf8');
const major = /<MatGameMajor>(\d+)</.exec(props)?.[1] ?? '0';
const minor = /<MatGameMinor>(\d+)</.exec(props)?.[1] ?? '0';
const version = `${major}.${minor}`;

const source = join(root, 'sdk', 'js');
const out = resolve(process.cwd(), target);
if (existsSync(out)) rmSync(out, { recursive: true, force: true });
mkdirSync(out, { recursive: true });

const lines = [`version ${version}`];
for (const name of readdirSync(source).filter(f => f.endsWith('.js')).sort()) {
    const text = `// MatGame SDK ${version} – copied from MatGame/sdk/js, do not edit here.\n` + readFileSync(join(source, name), 'utf8').replace(/\r\n/g, '\n');
    writeFileSync(join(out, name), text);
    lines.push(`${createHash('sha256').update(text).digest('hex')}  ${name}`);
}
writeFileSync(join(out, 'matgame.version'), lines.join('\n') + '\n');
console.log(`MatGame SDK ${version} → ${out} (${lines.length - 1} files)`);
