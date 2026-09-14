const {test} = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const bake = require('../desktop/bake-version.cjs');

test('bake-version writes the release tag and SHA into the packaged UI and package.json', () => {
  const files = {
    [path.join('/app', 'index.html')]: '<span class="preview" id="app-version"></span>',
    [path.join('/app', 'package.json')]: '{"name":"skua-mac","version":"0.2.0"}'
  };
  const fs = {
    readFileSync(file) {
      if (!files[file]) throw new Error('missing ' + file);
      return files[file];
    },
    writeFileSync(file, text) { files[file] = text; }
  };
  const result = bake.applyBakedFiles({
    fs,
    path,
    appDir: '/app',
    version: '0.2.4',
    commit: '2c3a10cabcdef0123456789abcdef0123456789'
  });
  assert.deepEqual(result, {label: 'v0.2.4 · 2c3a10c', version: '0.2.4', commit: '2c3a10cabcdef0123456789abcdef0123456789'});
  assert.equal(files[path.join('/app', 'index.html')], '<span class="preview" id="app-version">v0.2.4 · 2c3a10c</span>');
  assert.match(files[path.join('/app', 'package.json')], /"version": "0\.2\.4"/);
});
