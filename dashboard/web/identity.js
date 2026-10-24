// Sensor references shared by the local dashboard, Workspace, and fleet sources.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (root) root.SQSensorIdentity = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
  'use strict';
  const RAW_ID_LIMIT = 192;
  const PEER_KEY = /^[a-z0-9][a-z0-9_-]{0,31}$/;
  const isPeerKey = value => typeof value === 'string' && PEER_KEY.test(value) && value !== 'local';

  function persistenceKey(sourceKey, rawId) {
    if (typeof rawId !== 'string' || !rawId || (sourceKey !== 'local' && !isPeerKey(sourceKey)))
      throw new TypeError('A sensor identity needs a source key and nonempty raw ID.');
    return sourceKey === 'local' ? rawId : 'fleet:' + sourceKey + '|' + rawId;
  }
  function parseIdentity(id) {
    if (typeof id !== 'string' || !id) return null;
    if (!id.startsWith('fleet:')) return {sourceKey:'local', rawId:id};
    const separator = id.indexOf('|', 6);
    const sourceKey = id.slice(6, separator), rawId = id.slice(separator + 1);
    return separator >= 0 && isPeerKey(sourceKey) && rawId ? {sourceKey,rawId} : null;
  }
  function isWorkspaceId(id) {
    const identity = parseIdentity(id);
    return identity !== null && identity.rawId.length <= RAW_ID_LIMIT;
  }
  return Object.freeze({RAW_ID_LIMIT, isPeerKey, persistenceKey, parseIdentity, isWorkspaceId});
});
