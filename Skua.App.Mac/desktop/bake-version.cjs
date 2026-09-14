'use strict';

const updateCheck = require('./update-check.cjs');

function applyBakedFiles({fs, path, appDir, version, commit}) {
  if (typeof version !== 'string' || !version) throw new Error('version is required');
  const normalized = version.replace(/^v/i, '');
  if (!/^[0-9]+(\.[0-9]+)*$/.test(normalized)) throw new Error('version must be dotted numeric');
  const identity = {version: normalized, commit: commit || '', tag: 'v' + normalized};
  const label = updateCheck.formatIdentityLabel(identity);
  const htmlPath = path.join(appDir, 'index.html');
  const pkgPath = path.join(appDir, 'package.json');
  const html = fs.readFileSync(htmlPath, 'utf8');
  if (!html.includes('id="app-version"')) throw new Error('index.html is missing app-version');
  fs.writeFileSync(htmlPath, html.replace(/id="app-version">[^<]*</, 'id="app-version">' + label + '<'));
  const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));
  pkg.version = normalized;
  fs.writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n');
  return {label, version: normalized, commit: identity.commit};
}

if (require.main === module) {
  const fs = require('fs');
  const path = require('path');
  const result = applyBakedFiles({
    fs,
    path,
    appDir: process.argv[2],
    version: process.argv[3],
    commit: process.argv[4] || ''
  });
  process.stdout.write('Baked ' + result.label + '\n');
}

module.exports = {applyBakedFiles};
