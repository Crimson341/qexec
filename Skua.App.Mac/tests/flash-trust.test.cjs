const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const flashTrust = require('../desktop/flash-trust.cjs');

function tempUserData() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'skua-flash-trust-'));
}

test('ensureFlashTrust appends the current SWF and keeps existing entries', () => {
  const userData = tempUserData();
  const first = path.join(userData, 'first.swf');
  const second = path.join(userData, 'second.swf');
  fs.writeFileSync(first, 'swf');
  fs.writeFileSync(second, 'swf');
  const firstResult = flashTrust.ensureFlashTrust({fs, path, userData, swfPath: first});
  assert.equal(firstResult.ok, true);
  assert.equal(firstResult.added, true);
  const again = flashTrust.ensureFlashTrust({fs, path, userData, swfPath: first});
  assert.equal(again.added, false);
  const secondResult = flashTrust.ensureFlashTrust({fs, path, userData, swfPath: second});
  assert.equal(secondResult.added, true);
  const lines = fs.readFileSync(firstResult.configPath, 'utf8').trim().split('\n');
  assert.deepEqual(lines, [fs.realpathSync(first), fs.realpathSync(second)]);
  fs.rmSync(userData, {recursive: true, force: true});
});

test('ensureFlashTrust reports a missing SWF without writing a trust file', () => {
  const userData = tempUserData();
  const missing = path.join(userData, 'missing.swf');
  const result = flashTrust.ensureFlashTrust({fs, path, userData, swfPath: missing});
  assert.equal(result.ok, false);
  assert.equal(result.reason, 'swf-missing');
  assert.equal(fs.existsSync(flashTrust.trustConfigPath(userData, path)), false);
  fs.rmSync(userData, {recursive: true, force: true});
});
