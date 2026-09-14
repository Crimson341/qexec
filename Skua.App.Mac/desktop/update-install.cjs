'use strict';

const MAX_ZIP_BYTES = 400 * 1024 * 1024;
const ZIP_NAME = /^qexec-[A-Za-z0-9._-]+-macos-apple-silicon\.zip$/;

function shQuote(value) {
  return "'" + String(value).replace(/'/g, `'\\''`) + "'";
}

function resolveInstalledApp(execPath, path) {
  if (typeof execPath !== 'string' || !execPath || !path) return '';
  const macos = path.dirname(execPath);
  if (path.basename(macos) !== 'MacOS') return '';
  const contents = path.dirname(macos);
  if (path.basename(contents) !== 'Contents') return '';
  const bundle = path.dirname(contents);
  return path.basename(bundle).endsWith('.app') ? bundle : '';
}

function zipFileName(url) {
  let name = '';
  try { name = decodeURIComponent(new URL(url).pathname.split('/').pop() || ''); }
  catch (_error) { name = ''; }
  if (!ZIP_NAME.test(name)) throw new Error('Unexpected update package name');
  return name;
}

function isQexecBundle(fs, path, bundle) {
  if (!bundle || !fs.existsSync(path.join(bundle, 'Contents', 'MacOS'))) return false;
  const plist = path.join(bundle, 'Contents', 'Info.plist');
  if (!fs.existsSync(plist)) return false;
  try {
    const text = fs.readFileSync(plist, 'utf8');
    return text.includes('org.skua.mac.dev') || /<string>qexec<\/string>/.test(text);
  } catch (_error) {
    return false;
  }
}

function findExtractedApp(fs, path, extractDir) {
  const direct = path.join(extractDir, 'qexec.app');
  if (isQexecBundle(fs, path, direct)) return direct;
  let names;
  try { names = fs.readdirSync(extractDir); }
  catch (_error) { return ''; }
  for (let index = 0; index < names.length; index++) {
    const name = names[index];
    const full = path.join(extractDir, name);
    if (name.endsWith('.app') && isQexecBundle(fs, path, full)) return full;
  }
  return '';
}

function writeReplaceScript(fs, file, {pid, sourceApp, targetApp, cleanup}) {
  const processId = Number(pid);
  if (!Number.isInteger(processId) || processId <= 0) throw new Error('Update is missing the app process');
  if (!sourceApp || !targetApp || !cleanup) throw new Error('Update paths are incomplete');
  const script = [
    '#!/bin/bash',
    'set -euo pipefail',
    'pid=' + processId,
    'source=' + shQuote(sourceApp),
    'target=' + shQuote(targetApp),
    'cleanup=' + shQuote(cleanup),
    'for _ in $(/usr/bin/seq 1 50); do /bin/kill -0 "$pid" 2>/dev/null || break; /bin/sleep 0.2; done',
    'if /bin/kill -0 "$pid" 2>/dev/null; then exit 1; fi',
    '/usr/bin/ditto "$source" "$target"',
    '/usr/bin/codesign --force --deep --sign - "$target" || true',
    '/usr/bin/open "$target"',
    '/bin/rm -rf "$cleanup"'
  ].join('\n') + '\n';
  fs.writeFileSync(file, script, {encoding: 'utf8', mode: 0o755});
  return script;
}

function downloadZip(net, url, dest, headers, fs, timeoutMs) {
  return new Promise((resolve, reject) => {
    if (!net || typeof net.request !== 'function') {
      return reject(new Error('Download client is unavailable'));
    }
    let request;
    try { request = net.request({method: 'GET', url}); }
    catch (error) { return reject(error); }
    const extraHeaders = headers || {};
    for (const name of Object.keys(extraHeaders)) request.setHeader(name, extraHeaders[name]);
    request.setHeader('Accept', 'application/octet-stream');
    const limit = timeoutMs || 10 * 60 * 1000;
    const file = fs.createWriteStream(dest);
    let received = 0;
    const fail = error => {
      clearTimeout(timer);
      try { request.abort(); } catch (_error) { /* already closed */ }
      try { file.destroy(); } catch (_error) { /* already closed */ }
      try { fs.unlinkSync(dest); } catch (_error) { /* missing */ }
      reject(error);
    };
    const timer = setTimeout(() => fail(new Error('Update download timed out')), limit);
    request.on('response', response => {
      const status = response.statusCode;
      if (status < 200 || status >= 300) {
        return fail(new Error('Update download HTTP ' + status));
      }
      const length = Number(response.headers && (response.headers['content-length'] || response.headers['Content-Length']));
      if (Number.isFinite(length) && length > MAX_ZIP_BYTES) {
        return fail(new Error('Update package is too large'));
      }
      response.on('data', chunk => {
        received += chunk.length;
        if (received > MAX_ZIP_BYTES) return fail(new Error('Update package is too large'));
        file.write(chunk);
      });
      response.on('end', () => {
        file.end(() => {
          clearTimeout(timer);
          if (received < 4) return fail(new Error('Update package was empty'));
          resolve(received);
        });
      });
      response.on('error', fail);
    });
    request.on('error', fail);
    request.on('abort', () => fail(new Error('Update download aborted')));
    request.end();
  });
}

function run(execFile, command, args, timeoutMs) {
  return new Promise((resolve, reject) => {
    execFile(command, args, {timeout: timeoutMs || 120000}, (error, stdout, stderr) => {
      if (error) {
        error.stderr = stderr;
        return reject(error);
      }
      resolve(stdout);
    });
  });
}

async function applyPackagedUpdate({
  net, url, headers, execPath, pid, spawn, execFile, fs, path, tmpdir, app, download
}) {
  const targetApp = resolveInstalledApp(execPath, path);
  if (!targetApp) throw new Error('Packaged qexec.app was not found. Run the installed app to apply updates.');
  const work = path.join(tmpdir, 'qexec-update-' + pid);
  const extractDir = path.join(work, 'extract');
  const zipPath = path.join(work, zipFileName(url));
  const scriptPath = path.join(work, 'replace.sh');
  fs.mkdirSync(extractDir, {recursive: true});
  const getZip = download || downloadZip;
  await getZip(net, url, zipPath, headers, fs, 10 * 60 * 1000);
  await run(execFile, '/usr/bin/ditto', ['-x', '-k', zipPath, extractDir], 180000);
  const sourceApp = findExtractedApp(fs, path, extractDir);
  if (!sourceApp) throw new Error('The update zip did not contain qexec.app');
  if (path.resolve(sourceApp) === path.resolve(targetApp)) {
    throw new Error('Update extracted over the running app');
  }
  writeReplaceScript(fs, scriptPath, {pid, sourceApp, targetApp, cleanup: work});
  const child = spawn('/bin/bash', [scriptPath], {detached: true, stdio: 'ignore'});
  if (child && typeof child.unref === 'function') child.unref();
  if (app && typeof app.quit === 'function') app.quit();
  return {sourceApp, targetApp, scriptPath};
}

module.exports = {
  MAX_ZIP_BYTES,
  shQuote,
  resolveInstalledApp,
  zipFileName,
  isQexecBundle,
  findExtractedApp,
  writeReplaceScript,
  downloadZip,
  applyPackagedUpdate
};
