'use strict';

const MAX_ZIP_BYTES = 400 * 1024 * 1024;
const MAX_REDIRECTS = 5;
const ZIP_NAME = /^qexec-[A-Za-z0-9._-]+-macos-apple-silicon\.zip$/;
const ZIP_MAGIC = Buffer.from('PK');

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

function isAllowedRedirectUrl(url) {
  if (typeof url !== 'string' || !url) return false;
  try {
    const parsed = new URL(url);
    const host = parsed.hostname;
    return parsed.protocol === 'https:'
      && !parsed.username
      && !parsed.password
      && (host === 'github.com'
        || host === 'api.github.com'
        || host.endsWith('.githubusercontent.com'));
  } catch (_error) {
    return false;
  }
}

function headerValue(headers, name) {
  if (!headers) return '';
  const direct = headers[name] || headers[name.toLowerCase()];
  if (Array.isArray(direct)) return String(direct[0] || '');
  return direct == null ? '' : String(direct);
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

function writeFinderReplaceScript(fs, file) {
  const script = [
    'on run argv',
    '  set srcPath to item 1 of argv',
    '  set destFolderPath to item 2 of argv',
    '  set destAppPath to item 3 of argv',
    '  tell application "Finder"',
    '    if exists POSIX file destAppPath then delete POSIX file destAppPath',
    '    duplicate (POSIX file srcPath as alias) to (POSIX file destFolderPath as alias)',
    '  end tell',
    'end run',
    ''
  ].join('\n');
  fs.writeFileSync(file, script, {encoding: 'utf8'});
  return script;
}

function writeReplaceScript(fs, file, {pid, sourceApp, targetApp, cleanup, finderScript}) {
  const processId = Number(pid);
  if (!Number.isInteger(processId) || processId <= 0) throw new Error('Update is missing the app process');
  if (!sourceApp || !targetApp || !cleanup || !finderScript) throw new Error('Update paths are incomplete');
  const script = [
    '#!/bin/bash',
    'set -euo pipefail',
    'pid=' + processId,
    'source=' + shQuote(sourceApp),
    'target=' + shQuote(targetApp),
    'cleanup=' + shQuote(cleanup),
    'finder=' + shQuote(finderScript),
    'log="${cleanup}/replace.log"',
    'err="${cleanup}/ditto.err"',
    'say() { printf \'%s %s\\n\' "$(/bin/date -u +%Y-%m-%dT%H:%M:%SZ)" "$*" >> "$log"; }',
    'say "waiting for pid $pid to quit"',
    'for _ in $(/usr/bin/seq 1 150); do /bin/kill -0 "$pid" 2>/dev/null || break; /bin/sleep 0.2; done',
    'if /bin/kill -0 "$pid" 2>/dev/null; then say "asking pid $pid to exit"; /bin/kill -15 "$pid" 2>/dev/null || true; /bin/sleep 2; fi',
    'if /bin/kill -0 "$pid" 2>/dev/null; then say "force-stopping pid $pid"; /bin/kill -9 "$pid" 2>/dev/null || true; /bin/sleep 1; fi',
    'if /bin/kill -0 "$pid" 2>/dev/null; then say "app is still running"; exit 1; fi',
    'say "copying update onto $target"',
    'if ! /usr/bin/ditto "$source" "$target" 2>"$err"; then',
    '  say "ditto failed: $(/bin/cat "$err")"',
    '  dest_folder="$(/usr/bin/dirname "$target")"',
    '  /usr/bin/osascript "$finder" "$source" "$dest_folder" "$target"',
    '  say "Finder replaced the app after a blocked ditto"',
    'fi',
    '/usr/bin/xattr -dr com.apple.quarantine "$target" || true',
    '/usr/bin/codesign --force --deep --sign - "$target" || true',
    'say "relaunching"',
    '/usr/bin/open "$target"',
    '/bin/rm -rf "$cleanup"'
  ].join('\n') + '\n';
  fs.writeFileSync(file, script, {encoding: 'utf8', mode: 0o755});
  return script;
}

function isZipBuffer(chunk) {
  return !!chunk && chunk.length >= 2 && chunk[0] === ZIP_MAGIC[0] && chunk[1] === ZIP_MAGIC[1];
}

function downloadZip(net, url, dest, headers, fs, timeoutMs, redirectsLeft) {
  return new Promise((resolve, reject) => {
    if (!net || typeof net.request !== 'function') {
      return reject(new Error('Download client is unavailable'));
    }
    const remaining = redirectsLeft == null ? MAX_REDIRECTS : redirectsLeft;
    let request;
    try {
      // Electron 11 ClientRequest: redirect defaults to "follow". GitHub's
      // browser_download_url still 302s to release-assets.githubusercontent.com
      // and official asset docs require handling 200 or 302.
      request = net.request({method: 'GET', url, redirect: 'follow'});
    } catch (error) { return reject(error); }
    const extraHeaders = headers || {};
    for (const name of Object.keys(extraHeaders)) {
      if (name.toLowerCase() === 'accept') continue;
      request.setHeader(name, extraHeaders[name]);
    }
    request.setHeader('Accept', 'application/octet-stream');
    const limit = timeoutMs || 10 * 60 * 1000;
    const file = fs.createWriteStream(dest);
    let received = 0;
    let sawZip = false;
    let settled = false;
    const fail = error => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      try { request.abort(); } catch (_error) { /* already closed */ }
      try { file.destroy(); } catch (_error) { /* already closed */ }
      try { fs.unlinkSync(dest); } catch (_error) { /* missing */ }
      reject(error);
    };
    const timer = setTimeout(() => fail(new Error('Update download timed out')), limit);
    request.on('redirect', (_status, _method, redirectUrl) => {
      if (!isAllowedRedirectUrl(redirectUrl)) {
        return fail(new Error('Update download redirected to an unexpected host'));
      }
      request.followRedirect();
    });
    request.on('response', response => {
      const status = response.statusCode;
      if (status >= 300 && status < 400) {
        const location = headerValue(response.headers, 'location');
        if (!isAllowedRedirectUrl(location) || remaining <= 0) {
          return fail(new Error('Update download HTTP ' + status));
        }
        clearTimeout(timer);
        try { file.destroy(); } catch (_error) { /* already closed */ }
        try { fs.unlinkSync(dest); } catch (_error) { /* missing */ }
        if (settled) return;
        settled = true;
        downloadZip(net, location, dest, headers, fs, limit, remaining - 1).then(resolve, reject);
        return;
      }
      if (status < 200 || status >= 300) {
        return fail(new Error('Update download HTTP ' + status));
      }
      const length = Number(headerValue(response.headers, 'content-length'));
      if (Number.isFinite(length) && length > MAX_ZIP_BYTES) {
        return fail(new Error('Update package is too large'));
      }
      response.on('data', chunk => {
        if (!chunk || !chunk.length) return;
        if (!sawZip) {
          if (!isZipBuffer(chunk)) return fail(new Error('Update package was not a zip'));
          sawZip = true;
        }
        received += chunk.length;
        if (received > MAX_ZIP_BYTES) return fail(new Error('Update package is too large'));
        const ok = file.write(chunk);
        if (!ok && typeof response.pause === 'function') {
          response.pause();
          file.once('drain', () => {
            if (typeof response.resume === 'function') response.resume();
          });
        }
      });
      response.on('end', () => {
        file.end(() => {
          if (settled) return;
          clearTimeout(timer);
          if (received < 4 || !sawZip) return fail(new Error('Update package was empty'));
          settled = true;
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

async function fetchZip(getZip, net, urls, dest, headers, fs) {
  let lastError = new Error('Update download failed');
  for (let index = 0; index < urls.length; index++) {
    const url = urls[index];
    if (!url) continue;
    try {
      await getZip(net, url, dest, headers, fs, 10 * 60 * 1000);
      return url;
    } catch (error) {
      lastError = error;
    }
  }
  throw lastError;
}

async function applyPackagedUpdate({
  net, url, fallbackUrl, headers, execPath, pid, spawn, execFile, fs, path, tmpdir, app, download
}) {
  const targetApp = resolveInstalledApp(execPath, path);
  if (!targetApp) throw new Error('Packaged qexec.app was not found. Run the installed app to apply updates.');
  const work = path.join(tmpdir, 'qexec-update-' + pid);
  const extractDir = path.join(work, 'extract');
  const zipPath = path.join(work, zipFileName(url));
  const scriptPath = path.join(work, 'replace.sh');
  fs.mkdirSync(extractDir, {recursive: true});
  const getZip = download || downloadZip;
  const urls = [url];
  if (fallbackUrl && fallbackUrl !== url) urls.push(fallbackUrl);
  await fetchZip(getZip, net, urls, zipPath, headers, fs);
  await run(execFile, '/usr/bin/ditto', ['-x', '-k', zipPath, extractDir], 180000);
  const sourceApp = findExtractedApp(fs, path, extractDir);
  if (!sourceApp) throw new Error('The update zip did not contain qexec.app');
  if (path.resolve(sourceApp) === path.resolve(targetApp)) {
    throw new Error('Update extracted over the running app');
  }
  const finderPath = path.join(work, 'replace.applescript');
  writeFinderReplaceScript(fs, finderPath);
  writeReplaceScript(fs, scriptPath, {pid, sourceApp, targetApp, cleanup: work, finderScript: finderPath});
  const child = spawn('/bin/bash', [scriptPath], {detached: true, stdio: 'ignore'});
  if (child && typeof child.unref === 'function') child.unref();
  if (app && typeof app.quit === 'function') app.quit();
  return {sourceApp, targetApp, scriptPath, finderPath};
}

module.exports = {
  MAX_ZIP_BYTES,
  MAX_REDIRECTS,
  shQuote,
  resolveInstalledApp,
  zipFileName,
  isAllowedRedirectUrl,
  isQexecBundle,
  findExtractedApp,
  writeFinderReplaceScript,
  writeReplaceScript,
  downloadZip,
  applyPackagedUpdate
};
