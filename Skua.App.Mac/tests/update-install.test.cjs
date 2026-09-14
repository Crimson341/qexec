const {test} = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const {EventEmitter} = require('node:events');
const install = require('../desktop/update-install.cjs');

test('resolves the packaged app bundle and rejects unexpected zip names', () => {
  assert.equal(
    install.resolveInstalledApp('/Applications/qexec.app/Contents/MacOS/Electron', path),
    '/Applications/qexec.app'
  );
  assert.equal(install.resolveInstalledApp('/usr/local/bin/electron', path), '');
  assert.equal(
    install.zipFileName('https://github.com/Crimson341/qexec/releases/download/v0.2.1/qexec-v0.2.1-macos-apple-silicon.zip'),
    'qexec-v0.2.1-macos-apple-silicon.zip'
  );
  assert.throws(
    () => install.zipFileName('https://github.com/Crimson341/qexec/releases/download/v0.2.1/setup.exe'),
    /Unexpected update package name/
  );
});

test('replace script waits for quit, copies qexec.app, and relaunches', () => {
  const written = {};
  const script = install.writeReplaceScript(
    {writeFileSync(file, text) { written.file = file; written.text = text; }},
    '/tmp/replace.sh',
    {
      pid: 4242,
      sourceApp: "/tmp/extract/qexec.app",
      targetApp: "/Applications/qexec.app",
      cleanup: '/tmp/qexec-update-4242'
    }
  );
  assert.equal(written.file, '/tmp/replace.sh');
  assert.match(written.text, /pid=4242/);
  assert.match(written.text, /\/usr\/bin\/ditto/);
  assert.match(written.text, /\/usr\/bin\/open/);
  assert.doesNotMatch(written.text, /github\.com/);
  assert.equal(script, written.text);
});

test('applyPackagedUpdate downloads the zip, extracts qexec.app, then quits', async () => {
  const files = new Map();
  files.set('/tmp/qexec-update-9/extract/qexec.app/Contents/Info.plist', '<string>qexec</string><string>org.skua.mac.dev</string>');
  files.set('/tmp/qexec-update-9/extract/qexec.app/Contents/MacOS/Electron', 'bin');
  const dirs = new Set();
  const fs = {
    mkdirSync(dir) { dirs.add(dir); },
    existsSync(file) {
      if (files.has(file)) return true;
      for (const key of files.keys()) {
        if (key.startsWith(file + '/')) return true;
      }
      return false;
    },
    readFileSync(file) {
      if (!files.has(file)) throw new Error('missing ' + file);
      return files.get(file);
    },
    writeFileSync(file, text) { files.set(file, text); },
    readdirSync() { return ['qexec.app']; }
  };
  let spawned = null;
  let quit = false;
  const downloaded = [];
  const result = await install.applyPackagedUpdate({
    net: {},
    url: 'https://github.com/Crimson341/qexec/releases/download/v0.2.1/qexec-v0.2.1-macos-apple-silicon.zip',
    headers: {},
    execPath: '/Applications/qexec.app/Contents/MacOS/Electron',
    pid: 9,
    spawn(command, args, options) {
      spawned = {command, args, options};
      return {unref() {}};
    },
    execFile(command, args, _opts, callback) { callback(null, command + ' ' + args.join(' '), ''); },
    fs,
    path,
    tmpdir: '/tmp',
    app: {quit() { quit = true; }},
    download: async (_net, url, dest) => { downloaded.push({url, dest}); }
  });
  assert.equal(result.targetApp, '/Applications/qexec.app');
  assert.equal(result.sourceApp, '/tmp/qexec-update-9/extract/qexec.app');
  assert.equal(downloaded[0].url, 'https://github.com/Crimson341/qexec/releases/download/v0.2.1/qexec-v0.2.1-macos-apple-silicon.zip');
  assert.equal(spawned.command, '/bin/bash');
  assert.equal(spawned.options.detached, true);
  assert.equal(quit, true);
});

test('Electron download writes zip bytes and rejects oversized packages', async () => {
  const chunks = [];
  const fs = {
    createWriteStream() {
      return {
        write(chunk) { chunks.push(chunk); },
        end(done) { if (typeof done === 'function') done(); },
        destroy() {}
      };
    },
    unlinkSync() {}
  };
  const request = new EventEmitter();
  request.setHeader = () => {};
  request.abort = () => {};
  request.end = function end() {
    const response = new EventEmitter();
    response.statusCode = 200;
    response.headers = {'content-length': '4'};
    this.emit('response', response);
    response.emit('data', Buffer.from('PK\x03\x04'));
    response.emit('end');
  };
  const bytes = await install.downloadZip(
    {request: ({url}) => { assert.match(url, /qexec-v0\.2\.1-macos-apple-silicon\.zip$/); return request; }},
    'https://github.com/Crimson341/qexec/releases/download/v0.2.1/qexec-v0.2.1-macos-apple-silicon.zip',
    '/tmp/qexec.zip',
    {'User-Agent': 'qexec/0.2.1'},
    fs,
    1000
  );
  assert.equal(bytes, 4);

  const huge = new EventEmitter();
  huge.setHeader = () => {};
  huge.abort = () => {};
  huge.end = function end() {
    const response = new EventEmitter();
    response.statusCode = 200;
    response.headers = {'content-length': String(install.MAX_ZIP_BYTES + 1)};
    this.emit('response', response);
  };
  await assert.rejects(
    () => install.downloadZip({request: () => huge}, 'https://github.com/Crimson341/qexec/releases/download/v0.2.1/qexec-v0.2.1-macos-apple-silicon.zip', '/tmp/big.zip', {}, fs, 1000),
    /too large/
  );
});
