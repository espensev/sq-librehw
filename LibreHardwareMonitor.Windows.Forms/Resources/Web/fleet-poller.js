// Independent browser telemetry sources. No peer route other than data.json.
(function (root, factory) {
  'use strict';
  const fleet = typeof module === 'object' && module.exports ? require('./fleet.js') : root.SQFleet;
  const api = factory(root, fleet);
  if (root) root.SQFleetPoller = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis, function (root, Fleet) {
  'use strict';

  const LOCAL = Object.freeze({key:'local', label:'Local', url:null});
  const hasOwn = (value, key) => Object.prototype.hasOwnProperty.call(value, key);
  const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);

  function validateTelemetry(data) {
    if (!object(data) || !Array.isArray(data.Children) || data.Children.length !== 1)
      throw new Error('Telemetry must contain one machine tree.');
    const visited = new Set(), sensorIds = new Set(), pending = [data];
    while (pending.length) {
      const node = pending.pop();
      if (!object(node) || visited.has(node) || !Array.isArray(node.Children) || typeof node.Text !== 'string')
        throw new Error('Telemetry contains a malformed tree.');
      visited.add(node);
      if (hasOwn(node, 'HardwareId') && (typeof node.HardwareId !== 'string' || !node.HardwareId.length))
        throw new Error('Telemetry contains an invalid hardware identity.');
      if (hasOwn(node, 'SensorId')) {
        if (typeof node.SensorId !== 'string' || !node.SensorId.length ||
            typeof node.Type !== 'string' || !node.Type.length)
          throw new Error('Telemetry contains an invalid sensor identity or type.');
        if (sensorIds.has(node.SensorId)) throw new Error('Telemetry contains duplicate sensor IDs.');
        sensorIds.add(node.SensorId);
        for (const key of ['RawValue', 'RawMin', 'RawMax']) {
          if (hasOwn(node, key) && node[key] !== null && !Number.isFinite(node[key]))
            throw new Error('Telemetry contains an invalid sensor value.');
        }
      }
      for (const child of node.Children) pending.push(child);
    }
    return data;
  }

  function create(options) {
    const opts = options || {};
    const transport = opts.fetch || (root.fetch && root.fetch.bind(root));
    if (typeof transport !== 'function') throw new Error('Telemetry fetch is unavailable.');
    const now = opts.now || Date.now;
    const setTimer = opts.setTimeout || setTimeout, clearTimer = opts.clearTimeout || clearTimeout;
    const AbortCtor = opts.AbortController || root.AbortController;
    if (typeof AbortCtor !== 'function') throw new Error('AbortController is unavailable.');
    const onSample = opts.onSample || (() => {}), onGap = opts.onGap || (() => {});
    const onRemove = opts.onRemove || (() => {}), onState = opts.onState || (() => {});
    let intervalMs = Math.max(1, Number(opts.intervalMs) || 2000);
    const timeoutMs = Math.max(1, Number(opts.timeoutMs) || 15000);
    let paused = opts.paused === true, hidden = opts.hidden === true, running = false, disposed = false, sequence = 0;
    const originOptions = opts.pageOrigin === undefined ? {} : {pageOrigin:opts.pageOrigin};
    let fleet = Fleet.validateFleet(opts.fleet === undefined ? Fleet.defaults() : opts.fleet, originOptions);
    const entries = new Map(), retired = new Map();

    function entry(source) {
      return {source, lastGood:null, lastSuccess:null, error:null, generation:0, ingestionGeneration:0,
        active:null, scheduled:null, freshness:null, next:null, forcePending:false, removed:false, awaitingFresh:false};
    }
    entries.set('local', entry(LOCAL));
    fleet.sources.forEach(source => entries.set(source.key, entry(source)));
    const enabled = value => !value.removed && (value.source.key === 'local' || fleet.enabled);
    const canRun = (value, forced) => running && !disposed && !hidden && enabled(value) && (!paused || forced);

    function states() {
      const time = now();
      return [...entries.values()].map(value => {
        const suspended = !running || disposed || paused || hidden || !enabled(value) || value.awaitingFresh;
        const expired = value.lastSuccess !== null && time - value.lastSuccess >= intervalMs + timeoutMs;
        const status = suspended ? 'paused' : value.lastGood === null
          ? (value.error ? 'unavailable' : 'pending') : value.error || expired ? 'stale' : 'current';
        return {source:{...value.source}, status, lastGood:value.lastGood, lastSuccess:value.lastSuccess,
          error:value.error || (status === 'stale' && expired ? 'No recent telemetry response.' : null),
          generation:value.generation, ingestionGeneration:value.ingestionGeneration,
          active:!!value.active, scheduled:value.scheduled !== null};
      });
    }
    const publish = () => onState(states());
    function clearScheduled(value) {
      if (value.scheduled !== null) clearTimer(value.scheduled);
      value.scheduled = null;
    }
    function armFreshness(value) {
      if (value.freshness !== null) clearTimer(value.freshness);
      value.freshness = null;
      if (!canRun(value, false) || value.lastSuccess === null || value.error || value.awaitingFresh) return;
      const delay = value.lastSuccess + intervalMs + timeoutMs - now();
      if (delay <= 0) return;
      value.freshness = setTimer(() => {
        value.freshness = null;
        onGap(value.source);
        publish();
      }, delay);
    }
    function schedule(value) {
      clearScheduled(value);
      if (!canRun(value, false) || value.active) return;
      value.scheduled = setTimer(() => { value.scheduled = null; poll(value, false); }, intervalMs);
    }
    function continuePolling(value, mode) {
      if (value.forcePending && canRun(value, true)) poll(value, true);
      else if (mode === 'immediate' && canRun(value, false)) poll(value, false);
      else schedule(value);
    }
    function cancel(value, mode, suspend = false) {
      value.generation = ++sequence;
      if (suspend && value.lastGood !== null) value.awaitingFresh = true;
      clearScheduled(value);
      armFreshness(value);
      value.next = mode;
      if (value.active) {
        value.active.cancelled = true;
        clearTimer(value.active.timeout);
        value.active.controller.abort();
      } else continuePolling(value, mode);
      onGap(value.source);
    }
    function failed(value, error) {
      value.awaitingFresh = false;
      value.error = (error instanceof Error ? error.message : String(error)) || 'Telemetry request failed.';
      armFreshness(value);
      onGap(value.source);
      publish();
    }
    function poll(value, forced) {
      if (!canRun(value, forced)) return;
      clearScheduled(value);
      if (value.active) {
        value.next = forced ? 'forced' : 'immediate';
        return;
      }
      value.next = null;
      value.generation = ++sequence;
      const record = {controller:new AbortCtor(), generation:value.generation, cancelled:false, timedOut:false, timeout:null};
      value.active = record;
      record.timeout = setTimer(() => {
        if (value.active !== record || record.cancelled) return;
        record.timedOut = true;
        if (forced) value.forcePending = false;
        record.controller.abort();
        failed(value, new Error('Telemetry request timeout.'));
      }, timeoutMs);
      const isCurrent = () => !record.cancelled && !record.timedOut && !value.removed && !disposed && running &&
        value.generation === record.generation;
      (async () => {
        try {
          const isPeer = value.source.key !== 'local';
          const init = {method:'GET', cache:'no-store', signal:record.controller.signal};
          if (isPeer) Object.assign(init, {credentials:'omit', redirect:'error'});
          const response = await transport(isPeer ? value.source.url + '/data.json' : 'data.json', init);
          if (!isCurrent()) return;
          if (!response || !response.ok) throw new Error('HTTP ' + (response ? response.status : 'response unavailable'));
          if (isPeer && response.redirected) throw new Error('Peer telemetry redirects are unsupported.');
          const contentType = response.headers && response.headers.get('content-type');
          if (typeof contentType !== 'string' || !/^application\/(?:[\w.+-]+\+)?json\s*(?:;|$)/i.test(contentType))
            throw new Error('Telemetry response is not JSON.');
          const data = validateTelemetry(await response.json());
          if (!isCurrent()) return;
          const completedAt = now();
          value.lastGood = data;
          value.lastSuccess = completedAt;
          value.error = null;
          value.awaitingFresh = false;
          value.ingestionGeneration++;
          if (forced) value.forcePending = false;
          armFreshness(value);
          onSample(value.source, data, {generation:record.generation, completedAt});
          publish();
        } catch (error) {
          if (isCurrent()) {
            if (forced) value.forcePending = false;
            failed(value, error);
          }
        } finally {
          clearTimer(record.timeout);
          if (value.active === record) value.active = null;
          if (value.removed) { retired.delete(value.source.key); return; }
          const mode = value.next;
          value.next = null;
          continuePolling(value, mode);
        }
      })();
    }
    function configure(next) {
      if (disposed) return;
      const knownOrigins = {};
      [...entries.values(), ...retired.values()].forEach(value => {
        if (value.source.key !== 'local') knownOrigins[value.source.key] = value.source.url;
      });
      const validated = Fleet.validateFleet(next, {...originOptions, knownOrigins});
      const wasEnabled = fleet.enabled, nextKeys = new Set(validated.sources.map(source => source.key));
      fleet = validated;
      for (const [key, value] of entries) {
        if (key === 'local' || nextKeys.has(key)) continue;
        value.removed = true;
        value.forcePending = false;
        cancel(value, null);
        entries.delete(key);
        if (value.active) retired.set(key, value);
        onRemove(value.source);
      }
      const ordered = new Map([['local', entries.get('local')]]);
      for (const source of validated.sources) {
        let value = entries.get(source.key);
        const added = !value;
        if (!value) {
          value = retired.get(source.key) || entry(source);
          retired.delete(source.key);
          value.removed = false;
          value.lastGood = null;
          value.lastSuccess = null;
          value.error = null;
          value.awaitingFresh = false;
        }
        value.source = source;
        ordered.set(source.key, value);
        if (!fleet.enabled && (wasEnabled || added)) { value.forcePending = false; cancel(value, null, true); }
        else if (fleet.enabled && (!wasEnabled || added)) {
          armFreshness(value);
          if (value.active) value.next = 'immediate'; else continuePolling(value, 'immediate');
        }
      }
      entries.clear();
      ordered.forEach((value, key) => entries.set(key, value));
      publish();
    }
    return {
      states, configure,
      start(snapshotWhenPaused) {
        if (disposed || running) return;
        running = true;
        entries.forEach(value => {
          value.forcePending = paused && snapshotWhenPaused === true && enabled(value);
          armFreshness(value);
          continuePolling(value, 'immediate');
        });
        publish();
      },
      // Explicit refresh requests one snapshot while paused; it never resumes the timer.
      refresh() {
        if (disposed || !running) return;
        entries.forEach(value => {
          if (paused && enabled(value)) value.forcePending = true;
          if (hidden) return;
          poll(value, paused);
        });
      },
      setPaused(next) {
        next = next === true;
        if (disposed || paused === next) return;
        paused = next;
        entries.forEach(value => { value.forcePending = false; cancel(value, next ? null : 'immediate', next); });
        publish();
      },
      setHidden(next) {
        next = next === true;
        if (disposed || hidden === next) return;
        hidden = next;
        entries.forEach(value => cancel(value, next ? null : 'immediate', next));
        publish();
      },
      setInterval(value) {
        if (disposed) return;
        intervalMs = Math.max(1, Number(value) || intervalMs);
        entries.forEach(item => cancel(item, 'scheduled'));
        publish();
      },
      stop() {
        if (!running) return;
        running = false;
        entries.forEach(value => { value.forcePending = false; cancel(value, null, true); });
        publish();
      },
      dispose() { this.stop(); disposed = true; }
    };
  }
  return {create, validateTelemetry};
});
