// SQ Fleet - browser-local source configuration and persistence identity.
(function (root, factory) {
  'use strict';
  const identity = typeof module === 'object' && module.exports ? require('./identity.js') : root.SQSensorIdentity;
  const api = factory(root, identity);
  if (root) root.SQFleet = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : (typeof globalThis !== 'undefined' ? globalThis : this), function (root, Identity) {
  'use strict';

  const SCHEMA = 'sq.fleet';
  const VERSION = 1;
  const LIMITS = Object.freeze({sources:8, keyLength:32, labelLength:80, urlLength:2048,
    documentBytes:32 * 1024, rawIdLength:Identity.RAW_ID_LIMIT, retainedOrigins:4096});
  const own = (value, key) => Object.prototype.hasOwnProperty.call(value, key);
  const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
  const peerKey = Identity.isPeerKey;
  const {parseIdentity, isWorkspaceId} = Identity;

  class FleetError extends Error {
    constructor(code, message) { super(message); this.name = 'FleetError'; this.code = code; }
  }
  function fail(code, message) { throw new FleetError(code, message); }
  function fields(value, expected, description) {
    if (!object(value) || Object.keys(value).length !== expected.length ||
        expected.some(key => !own(value, key))) fail('invalid-fields', description + ' has invalid fields.');
  }
  function defaults() { return {enabled:false, sources:[]}; }

  function canonicalOrigin(value) {
    if (typeof value !== 'string' || value.length > LIMITS.urlLength ||
        !/^https:\/\/[^\s/?#\\@]+\/?$/i.test(value))
      fail('invalid-url', 'Use an HTTPS origin without credentials, path, query, or fragment.');
    let url;
    try { url = new URL(value); } catch { fail('invalid-url', 'The HTTPS origin is invalid.'); }
    if (url.protocol !== 'https:' || !url.hostname || url.username || url.password ||
        url.pathname !== '/' || url.search || url.hash)
      fail('invalid-url', 'Use an HTTPS origin without credentials, path, query, or fragment.');
    return url.origin;
  }
  function pageOrigin(options) {
    const value = own(options, 'pageOrigin') ? options.pageOrigin : root?.location?.origin;
    if (value === undefined || value === null || value === '' || value === 'null') return null;
    try { return new URL(value).origin; } catch { fail('invalid-page-origin', 'The dashboard origin is invalid.'); }
  }
  function knownOrigins(value) {
    if (value === undefined) return {};
    if (!object(value) || Object.keys(value).length > LIMITS.retainedOrigins)
      fail('invalid-origins', 'Stored fleet origin associations are invalid or exceed the limit.');
    const result = {};
    for (const key of Object.keys(value)) {
      if (!peerKey(key)) fail('invalid-origins', 'A stored fleet source key is invalid.');
      result[key] = value[key] === null ? null : canonicalOrigin(value[key]);
    }
    return result;
  }
  function validateSources(value, options = {}) {
    if (!Array.isArray(value) || value.length > LIMITS.sources)
      fail('source-count', 'A fleet can contain at most eight peers.');
    const origins = knownOrigins(options.knownOrigins);
    const ownOrigin = pageOrigin(options), keys = new Set(), urls = new Set();
    return value.map(source => {
      fields(source, ['key','label','url'], 'A source');
      if (!peerKey(source.key)) fail('invalid-key', 'Source keys must be 1–32 lowercase ASCII letters, digits, underscores, or hyphens; local is reserved.');
      if (typeof source.label !== 'string' || !source.label.trim() || source.label.trim().length > LIMITS.labelLength)
        fail('invalid-label', 'A source label must contain 1–80 characters.');
      const url = canonicalOrigin(source.url);
      if (url === ownOrigin) fail('local-origin', 'This dashboard already monitors its local source.');
      if (keys.has(source.key)) fail('duplicate-key', 'Source keys must be unique.');
      if (urls.has(url)) fail('duplicate-origin', 'Source origins must be unique.');
      if (own(origins, source.key) && origins[source.key] !== url)
        fail('source-rebound', 'A source key cannot be assigned to another origin or an unknown saved reference.');
      keys.add(source.key); urls.add(url);
      return {key:source.key, label:source.label.trim(), url};
    });
  }
  function validateFleet(value, options = {}) {
    fields(value, ['enabled','sources'], 'Fleet configuration');
    if (typeof value.enabled !== 'boolean') fail('invalid-enabled', 'Fleet enabled must be a boolean.');
    return {enabled:value.enabled, sources:validateSources(value.sources, options)};
  }

  // Missing associations become tombstones: importing a profile never grants
  // permission to bind its absent source key to a newly supplied endpoint.
  function retainOrigins(previous, sources, referenceIds) {
    const before = knownOrigins(previous);
    const current = validateSources(sources, {knownOrigins:before, pageOrigin:null});
    const keep = referenceIds === undefined ? new Set(Object.keys(before)) : new Set();
    if (referenceIds !== undefined) {
      if (!Array.isArray(referenceIds)) fail('invalid-references', 'Source references must be an array.');
      referenceIds.forEach(id => {
        const identity = parseIdentity(id);
        if (identity && identity.sourceKey !== 'local') keep.add(identity.sourceKey);
      });
    }
    const result = {};
    keep.forEach(key => { result[key] = own(before, key) ? before[key] : null; });
    current.forEach(source => { result[source.key] = source.url; });
    if (Object.keys(result).length > LIMITS.retainedOrigins)
      fail('origin-limit', 'Too many retained source references; remove unused preferences before adding more sources.');
    return result;
  }
  function normalize(value, options = {}) {
    let origins = {};
    try {
      origins = knownOrigins(options.knownOrigins);
      const fleet = value === undefined ? defaults() : validateFleet(value, {...options, knownOrigins:origins});
      origins = retainOrigins(origins, fleet.sources, options.referenceIds);
      return {fleet, origins, error:null};
    } catch (error) {
      return {fleet:defaults(), origins, error:error instanceof FleetError ? error.message : 'Invalid fleet configuration.'};
    }
  }

  function utf8ByteLength(text) {
    let bytes = 0;
    for (let i = 0; i < text.length; i++) {
      const code = text.charCodeAt(i);
      if (code < 0x80) bytes++;
      else if (code < 0x800) bytes += 2;
      else if (code >= 0xd800 && code <= 0xdbff && i + 1 < text.length &&
          text.charCodeAt(i + 1) >= 0xdc00 && text.charCodeAt(i + 1) <= 0xdfff) { bytes += 4; i++; }
      else bytes += 3;
    }
    return bytes;
  }
  function importSources(text, current, options = {}) {
    if (typeof text !== 'string' || utf8ByteLength(text) > LIMITS.documentBytes)
      fail('document-size', 'A fleet import must be at most 32 KiB of UTF-8.');
    const state = validateFleet(current, options);
    const origins = retainOrigins(options.knownOrigins, state.sources, options.referenceIds);
    let document;
    try { document = JSON.parse(text); } catch { fail('invalid-json', 'The fleet import is not valid JSON.'); }
    fields(document, ['schema','version','sources'], 'A fleet import');
    if (document.schema !== SCHEMA || document.version !== VERSION)
      fail('invalid-schema', 'The fleet import schema or version is unsupported.');
    return {enabled:state.enabled, sources:validateSources(document.sources, {...options, knownOrigins:origins})};
  }
  function exportSources(value, options = {}) {
    const fleet = validateFleet(value, options);
    return JSON.stringify({schema:SCHEMA, version:VERSION, sources:fleet.sources}, null, 2);
  }

  function nextKey(fleet, options) {
    const used = new Set(Object.keys(retainOrigins(options.knownOrigins, fleet.sources, options.referenceIds)));
    const generate = options.keyFactory || (() => {
      if (!root?.crypto?.getRandomValues) fail('key-unavailable', 'A secure source-key generator is unavailable.');
      const bytes = root.crypto.getRandomValues(new Uint8Array(16));
      return Array.from(bytes, n => n.toString(16).padStart(2, '0')).join('');
    });
    for (let attempt = 0; attempt < 16; attempt++) {
      const key = generate();
      if (peerKey(key) && !used.has(key)) return key;
    }
    fail('key-unavailable', 'Could not allocate a unique source key.');
  }
  function sourceIndex(fleet, key) {
    const index = fleet.sources.findIndex(source => source.key === key);
    if (index < 0) fail('source-missing', 'The peer source does not exist.');
    return index;
  }
  function addSource(value, source, options = {}) {
    const fleet = validateFleet(value, options);
    fields(source, ['label','url'], 'A new source');
    fleet.sources.push({key:nextKey(fleet, options), label:source.label, url:source.url});
    return validateFleet(fleet, options);
  }
  function renameSource(value, key, label, options = {}) {
    const fleet = validateFleet(value, options);
    fleet.sources[sourceIndex(fleet, key)].label = label;
    return validateFleet(fleet, options);
  }
  function moveSource(value, key, index, options = {}) {
    const fleet = validateFleet(value, options);
    const from = sourceIndex(fleet, key);
    if (!Number.isInteger(index) || index < 0 || index >= fleet.sources.length)
      fail('invalid-order', 'The source position is outside the machine list.');
    fleet.sources.splice(index, 0, fleet.sources.splice(from, 1)[0]);
    return fleet;
  }
  function removeSource(value, key, options = {}) {
    const fleet = validateFleet(value, options);
    fleet.sources.splice(sourceIndex(fleet, key), 1);
    return fleet;
  }
  function replaceSource(value, key, url, options = {}) {
    const fleet = validateFleet(value, options);
    const index = sourceIndex(fleet, key);
    fleet.sources[index] = {key:nextKey(fleet, options), label:fleet.sources[index].label, url};
    return validateFleet(fleet, options);
  }

  function persistenceKey(sourceKey, rawId) {
    if (typeof rawId !== 'string' || !rawId || (sourceKey !== 'local' && !peerKey(sourceKey)))
      fail('invalid-identity', 'A sensor identity needs a source key and nonempty raw ID.');
    return Identity.persistenceKey(sourceKey, rawId);
  }

  return Object.freeze({SCHEMA, VERSION, LIMITS, FleetError, defaults, canonicalOrigin,
    validateSources, validateFleet, normalize, retainOrigins, importSources, exportSources,
    addSource, renameSource, moveSource, removeSource, replaceSource,
    persistenceKey, parseIdentity, isWorkspaceId});
});
