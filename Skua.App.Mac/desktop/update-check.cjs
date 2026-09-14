'use strict';

const OWNER = 'Crimson341';
const REPO = 'qexec';
const API_ROOT = 'https://api.github.com/repos/' + OWNER + '/' + REPO;
const RELEASES_PAGE = 'https://github.com/' + OWNER + '/' + REPO + '/releases';
const API_PREFIX = API_ROOT + '/';

function githubHeaders(version) {
  const release = typeof version === 'string' && version ? version : '0';
  return {
    'Accept': 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
    'User-Agent': 'qexec/' + release + ' (+https://github.com/' + OWNER + '/' + REPO + ')'
  };
}

function isGithubApiUrl(url) {
  return typeof url === 'string' && url.startsWith(API_PREFIX);
}

function isAllowedUpdateUrl(url) {
  if (typeof url !== 'string' || url.length === 0 || url.length > 2081) return false;
  try {
    const parsed = new URL(url);
    const prefix = '/' + OWNER + '/' + REPO;
    const path = parsed.pathname;
    const inRepo = path === prefix || path === prefix + '/' || path.startsWith(prefix + '/');
    return parsed.protocol === 'https:'
      && parsed.hostname === 'github.com'
      && inRepo
      && !parsed.username
      && !parsed.password;
  } catch (_error) {
    return false;
  }
}

function isAllowedDownloadUrl(url) {
  if (!isAllowedUpdateUrl(url)) return false;
  try {
    const parsed = new URL(url);
    return parsed.pathname.startsWith('/' + OWNER + '/' + REPO + '/releases/download/')
      && /\/qexec-[A-Za-z0-9._-]+-macos-apple-silicon\.zip$/i.test(parsed.pathname);
  } catch (_error) {
    return false;
  }
}

function pickMacAsset(release) {
  if (!release || !Array.isArray(release.assets)) return null;
  return release.assets.find(asset =>
    asset
    && asset.state === 'uploaded'
    && typeof asset.name === 'string'
    && /macos-apple-silicon\.zip$/i.test(asset.name)
    && isAllowedDownloadUrl(asset.browser_download_url)
  ) || null;
}

function readJson(fs, file) {
  try {
    const value = JSON.parse(fs.readFileSync(file, 'utf8'));
    return value && typeof value === 'object' ? value : null;
  } catch (_error) {
    return null;
  }
}

function resolveIdentity({fs, path, dirname, env, execFileSync}) {
  const environment = env || {};
  let version = '0.0.0';
  let commit = typeof environment.QEXEC_COMMIT === 'string' ? environment.QEXEC_COMMIT.trim() : '';
  const pkg = readJson(fs, path.join(dirname, 'package.json'));
  if (pkg && typeof pkg.version === 'string' && pkg.version) version = pkg.version;
  const baked = readJson(fs, path.join(dirname, 'version.json'));
  if (baked) {
    if (!commit && typeof baked.commit === 'string') commit = baked.commit.trim();
    if (typeof baked.version === 'string' && baked.version) version = baked.version;
  }
  if (!commit && typeof execFileSync === 'function') {
    try {
      commit = String(execFileSync('git', ['-C', path.resolve(dirname, '../..'), 'rev-parse', 'HEAD'], {
        encoding: 'utf8',
        timeout: 2000,
        stdio: ['ignore', 'pipe', 'ignore']
      })).trim();
    } catch (_error) {
      commit = '';
    }
  }
  if (!/^[0-9a-f]{7,40}$/i.test(commit)) commit = '';
  const tag = 'v' + version;
  return {version, commit, tag, ref: commit || tag};
}

function formatIdentityLabel(identity) {
  if (!identity || typeof identity.tag !== 'string' || !identity.tag) return '';
  const sha = typeof identity.commit === 'string' && /^[0-9a-f]{7,40}$/i.test(identity.commit)
    ? identity.commit.slice(0, 7)
    : '';
  return sha ? identity.tag + ' · ' + sha : identity.tag;
}

function parseVersion(value) {
  return String(value || '').replace(/^v/i, '').split(/[.+-]/).slice(0, 3).map(part => {
    const number = parseInt(part, 10);
    return Number.isFinite(number) ? number : 0;
  });
}

function isNewerTag(tag, version) {
  const remote = parseVersion(tag);
  const local = parseVersion(version);
  for (let index = 0; index < 3; index++) {
    const left = remote[index] || 0;
    const right = local[index] || 0;
    if (left !== right) return left > right;
  }
  return false;
}

function comparePath(base, head) {
  const target = typeof head === 'string' && head ? head : 'main';
  return API_ROOT + '/compare/' + encodeURIComponent(base) + '...' + encodeURIComponent(target) + '?per_page=1';
}

function pickNewestRelease(releases) {
  if (!Array.isArray(releases)) return null;
  return releases.find(release =>
    release && !release.draft && isAllowedUpdateUrl(release.html_url)) || null;
}

function isSameRelease(identity, release) {
  return !!(identity && release && identity.tag && release.tag_name === identity.tag);
}

function releasePageUrl(release) {
  return release && isAllowedUpdateUrl(release.html_url) ? release.html_url : '';
}

function httpError(message, statusCode, body) {
  const error = new Error(message);
  error.statusCode = statusCode;
  error.body = body;
  return error;
}

function requestGithubJson(net, url, headers, timeoutMs) {
  return new Promise((resolve, reject) => {
    if (!net || typeof net.request !== 'function') {
      return reject(new Error('GitHub client is unavailable'));
    }
    if (!isGithubApiUrl(url)) {
      return reject(new Error('Refusing non-GitHub update URL'));
    }
    let request;
    try {
      request = net.request({method: 'GET', url});
    } catch (error) {
      return reject(error);
    }
    const extraHeaders = headers || {};
    for (const name of Object.keys(extraHeaders)) request.setHeader(name, extraHeaders[name]);
    const limit = timeoutMs || 10000;
    const timer = setTimeout(() => {
      try { request.abort(); } catch (_error) { /* already closed */ }
      reject(httpError('GitHub update check timed out', 0));
    }, limit);
    const chunks = [];
    request.on('response', response => {
      response.on('data', chunk => chunks.push(typeof chunk === 'string' ? chunk : chunk.toString('utf8')));
      response.on('end', () => {
        clearTimeout(timer);
        const body = chunks.join('');
        const status = response.statusCode;
        if (status < 200 || status >= 300) {
          return reject(httpError('GitHub HTTP ' + status, status, body));
        }
        try { resolve(JSON.parse(body)); }
        catch (error) { reject(error); }
      });
      response.on('error', error => { clearTimeout(timer); reject(error); });
    });
    request.on('error', error => { clearTimeout(timer); reject(error); });
    request.on('abort', () => { clearTimeout(timer); reject(httpError('GitHub update check aborted', 0)); });
    request.end();
  });
}

async function readCompare(getJson, identity) {
  const refs = [];
  if (identity.commit) refs.push(identity.commit);
  if (identity.tag && !refs.includes(identity.tag)) refs.push(identity.tag);
  let lastError = null;
  for (const ref of refs) {
    try {
      const compare = await getJson(comparePath(ref));
      if (compare && typeof compare.ahead_by === 'number') return compare;
      lastError = new Error('GitHub compare returned no ahead_by');
    } catch (error) {
      lastError = error;
      if (!error || error.statusCode !== 404) throw error;
    }
  }
  throw lastError || new Error('GitHub compare failed');
}

async function readNewestRelease(getJson) {
  return pickNewestRelease(await getJson(API_ROOT + '/releases?per_page=5'));
}

async function releaseAheadBy(getJson, identity, release) {
  if (!release || typeof release.tag_name !== 'string' || !release.tag_name) return 0;
  if (isSameRelease(identity, release)) return 0;
  const refs = [];
  if (identity.commit) refs.push(identity.commit);
  if (identity.tag && !refs.includes(identity.tag)) refs.push(identity.tag);
  for (const ref of refs) {
    try {
      const compare = await getJson(comparePath(ref, release.tag_name));
      if (compare && typeof compare.ahead_by === 'number') return compare.ahead_by;
    } catch (error) {
      if (!error || error.statusCode !== 404) throw error;
    }
  }
  return isNewerTag(release.tag_name, identity.version) ? 1 : 0;
}

async function chooseUpdateUrl(getJson, identity, compare, newest) {
  let release = newest;
  if (release === undefined) {
    try { release = await readNewestRelease(getJson); }
    catch (_error) { release = null; }
  }
  const releaseUrl = releasePageUrl(release);
  if (releaseUrl && !isSameRelease(identity, release) && isNewerTag(release.tag_name, identity.version)) {
    return releaseUrl;
  }
  if (isAllowedUpdateUrl(compare && compare.html_url)) return compare.html_url;
  return RELEASES_PAGE;
}

function updateMessage(identity, aheadBy, release) {
  const label = identity.commit ? identity.commit.slice(0, 7) : identity.tag;
  if (aheadBy > 0) {
    return 'Update available: main is ' + aheadBy + ' commit' + (aheadBy === 1 ? '' : 's')
      + ' ahead of this build (' + label + ').';
  }
  const tag = release && release.tag_name ? release.tag_name : 'latest';
  return 'Update available: GitHub release ' + tag + ' is newer than this build (' + label + ').';
}

async function findUpdate({getJson, identity}) {
  if (!identity || typeof getJson !== 'function') {
    throw new Error('Update check is missing identity or GitHub client');
  }
  const compare = await readCompare(getJson, identity);
  const aheadBy = Number(compare.ahead_by) || 0;
  let newest = null;
  try {
    newest = await readNewestRelease(getJson);
  } catch (_error) {
    newest = null;
  }
  let releaseAhead = 0;
  if (newest && !isSameRelease(identity, newest)) {
    if (isNewerTag(newest.tag_name, identity.version)) {
      releaseAhead = 1;
    } else if (aheadBy <= 0) {
      try {
        releaseAhead = await releaseAheadBy(getJson, identity, newest);
      } catch (_error) {
        releaseAhead = 0;
      }
    }
  }
  const asset = pickMacAsset(newest);
  const downloadUrl = asset ? asset.browser_download_url : '';
  const canInstall = !!downloadUrl && (releaseAhead > 0 || (aheadBy > 0 && !isSameRelease(identity, newest)));
  if (!canInstall) return {available: false, aheadBy: 0};
  return {
    available: true,
    aheadBy: aheadBy || releaseAhead,
    url: downloadUrl,
    downloadUrl,
    message: updateMessage(identity, aheadBy, newest)
  };
}

module.exports = {
  OWNER,
  REPO,
  API_ROOT,
  RELEASES_PAGE,
  githubHeaders,
  isAllowedUpdateUrl,
  isAllowedDownloadUrl,
  pickMacAsset,
  isGithubApiUrl,
  comparePath,
  pickNewestRelease,
  resolveIdentity,
  formatIdentityLabel,
  isNewerTag,
  requestGithubJson,
  findUpdate
};
