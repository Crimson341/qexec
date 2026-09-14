'use strict';

const TRUST_RELATIVE = ['Pepper Data', 'Shockwave Flash', 'WritableRoot', '#Security', 'FlashPlayerTrust', 'SkuaMac.cfg'];

function trustConfigPath(userData, pathImpl) {
  if (typeof userData !== 'string' || !userData) {
    throw new Error('userData is required');
  }
  return pathImpl.join(userData, ...TRUST_RELATIVE);
}

function resolveSwfPath(swfPath, fsImpl, pathImpl) {
  if (typeof swfPath !== 'string' || !swfPath) return '';
  const resolved = pathImpl.resolve(swfPath);
  if (typeof fsImpl.realpathSync === 'function' && fsImpl.existsSync(resolved)) {
    return fsImpl.realpathSync(resolved);
  }
  return resolved;
}

function readTrustLines(configPath, fsImpl) {
  if (!fsImpl.existsSync(configPath)) return [];
  const text = fsImpl.readFileSync(configPath, 'utf8');
  return String(text).split(/\r?\n/).map(line => line.trim()).filter(Boolean);
}

function ensureFlashTrust({fs: fsImpl, path: pathImpl, userData, swfPath}) {
  if (!fsImpl || !pathImpl) throw new Error('fs and path are required');
  if (typeof swfPath !== 'string' || !swfPath) {
    return {ok: false, reason: 'missing-swf-path'};
  }
  if (!fsImpl.existsSync(swfPath)) {
    return {ok: false, reason: 'swf-missing', path: swfPath};
  }
  const resolved = resolveSwfPath(swfPath, fsImpl, pathImpl);
  const configPath = trustConfigPath(userData, pathImpl);
  const lines = readTrustLines(configPath, fsImpl);
  if (lines.includes(resolved)) {
    return {ok: true, added: false, path: resolved, configPath};
  }
  fsImpl.mkdirSync(pathImpl.dirname(configPath), {recursive: true});
  const existing = fsImpl.existsSync(configPath) ? fsImpl.readFileSync(configPath, 'utf8') : '';
  const prefix = existing && !existing.endsWith('\n') ? existing + '\n' : existing;
  fsImpl.writeFileSync(configPath, prefix + resolved + '\n', 'utf8');
  return {ok: true, added: true, path: resolved, configPath};
}

module.exports = {trustConfigPath, ensureFlashTrust};
