const {test} = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const {EventEmitter} = require('node:events');
const update = require('../desktop/update-check.cjs');

test('identity prefers baked commit, then git, then the version tag', () => {
  const files = {
    [path.join('/app', 'package.json')]: '{"version":"0.2.0"}',
    [path.join('/app', 'version.json')]: '{"version":"0.2.0","commit":"abc1234def"}'
  };
  const baked = update.resolveIdentity({
    fs: {readFileSync: file => { if (!files[file]) throw new Error('missing'); return files[file]; }},
    path,
    dirname: '/app',
    env: {},
    execFileSync: () => { throw new Error('git should not run'); }
  });
  assert.deepEqual(baked, {version: '0.2.0', commit: 'abc1234def', tag: 'v0.2.0', ref: 'abc1234def'});

  const fromGit = update.resolveIdentity({
    fs: {readFileSync: file => file.endsWith('package.json') ? '{"version":"0.2.0"}' : (() => { throw new Error('no bake'); })()},
    path,
    dirname: '/app',
    env: {},
    execFileSync: (command, args) => {
      assert.equal(command, 'git');
      assert.equal(args[0], '-C');
      return 'ffffffffffffaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n';
    }
  });
  assert.equal(fromGit.ref, 'ffffffffffffaaaaaaaaaaaaaaaaaaaaaaaaaaaa');

  const tagged = update.resolveIdentity({
    fs: {readFileSync: file => file.endsWith('package.json') ? '{"version":"0.2.0"}' : (() => { throw new Error('no bake'); })()},
    path,
    dirname: '/app',
    env: {QEXEC_COMMIT: 'not-a-sha'},
    execFileSync: () => { throw new Error('offline git'); }
  });
  assert.deepEqual(tagged, {version: '0.2.0', commit: '', tag: 'v0.2.0', ref: 'v0.2.0'});
});

test('compare reports updates only when main is ahead, and opens a newer release when one exists', async () => {
  const identity = {version: '0.2.0', commit: '1111111', tag: 'v0.2.0', ref: '1111111'};
  const current = await update.findUpdate({
    identity,
    getJson: async url => {
      assert.equal(url, update.comparePath('1111111'));
      return {ahead_by: 0, status: 'identical', html_url: 'https://github.com/Crimson341/qexec/compare/1111111...main'};
    }
  });
  assert.deepEqual(current, {available: false, aheadBy: 0});

  const requested = [];
  const pending = await update.findUpdate({
    identity,
    getJson: async url => {
      requested.push(url);
      if (url.includes('/compare/')) {
        return {
          ahead_by: 3,
          status: 'ahead',
          html_url: 'https://github.com/Crimson341/qexec/compare/1111111...main'
        };
      }
      return [{
        draft: false,
        prerelease: true,
        tag_name: 'v0.3.0',
        html_url: 'https://github.com/Crimson341/qexec/releases/tag/v0.3.0'
      }];
    }
  });
  assert.equal(pending.available, true);
  assert.equal(pending.aheadBy, 3);
  assert.equal(pending.url, 'https://github.com/Crimson341/qexec/releases/tag/v0.3.0');
  assert.match(pending.message, /3 commits ahead of this build \(1111111\)/);
  assert.ok(requested[0].includes('1111111...main'));

  const sameRelease = await update.findUpdate({
    identity,
    getJson: async url => {
      if (url.includes('/compare/')) {
        return {ahead_by: 1, html_url: 'https://github.com/Crimson341/qexec/compare/v0.2.0...main'};
      }
      return [{draft: false, tag_name: 'v0.2.0', html_url: 'https://github.com/Crimson341/qexec/releases/tag/v0.2.0'}];
    }
  });
  assert.equal(sameRelease.url, 'https://github.com/Crimson341/qexec/compare/v0.2.0...main');
});

test('unknown local commit falls back to the version tag, and foreign URLs are rejected', async () => {
  const identity = {version: '0.2.0', commit: 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeef', tag: 'v0.2.0', ref: 'deadbeef'};
  const notice = await update.findUpdate({
    identity,
    getJson: async url => {
      if (url.includes('deadbeef')) {
        const error = new Error('missing');
        error.statusCode = 404;
        throw error;
      }
      if (url.includes('v0.2.0...main')) {
        return {ahead_by: 2, html_url: 'https://github.com/Crimson341/qexec/compare/v0.2.0...main'};
      }
      throw new Error('unexpected ' + url);
    }
  });
  assert.equal(notice.available, true);
  assert.equal(notice.aheadBy, 2);
  assert.equal(update.isAllowedUpdateUrl('https://github.com/Crimson341/qexec/releases/tag/v0.2.0'), true);
  assert.equal(update.isAllowedUpdateUrl('https://github.com/Crimson341/qexec/releases'), true);
  assert.equal(update.isAllowedUpdateUrl('https://evil.test/Crimson341/qexec/releases'), false);
  assert.equal(update.isAllowedUpdateUrl('https://github.com/evil/qexec/releases'), false);
  assert.equal(update.isAllowedUpdateUrl('https://github.com/Crimson341/qexec-evil/releases'), false);
  assert.equal(update.isNewerTag('v0.3.0', '0.2.0'), true);
  assert.equal(update.isNewerTag('v0.2.0', '0.2.0'), false);
  await assert.rejects(
    () => update.requestGithubJson({request() { return {}; }}, 'https://example.com/repos/x', {}),
    /Refusing non-GitHub update URL/
  );
});

test('Electron net getter sends GitHub headers and parses JSON', async () => {
  const headers = update.githubHeaders('0.2.0');
  assert.equal(headers['User-Agent'], 'qexec/0.2.0 (+https://github.com/Crimson341/qexec)');
  assert.equal(headers.Accept, 'application/vnd.github+json');
  const request = new EventEmitter();
  request.setHeader = function setHeader(name, value) {
    this.headers = this.headers || {};
    this.headers[name] = value;
  };
  request.end = function end() {
    const response = new EventEmitter();
    response.statusCode = 200;
    this.emit('response', response);
    response.emit('data', '{"ahead_by":1}');
    response.emit('end');
  };
  request.abort = () => {};
  const body = await update.requestGithubJson(
    {request: ({url}) => { assert.ok(url.startsWith(update.API_ROOT + '/')); return request; }},
    update.comparePath('v0.2.0'),
    headers,
    1000
  );
  assert.deepEqual(body, {ahead_by: 1});
  assert.equal(request.headers['User-Agent'], headers['User-Agent']);
});
