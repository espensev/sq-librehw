// Existing local dashboard polling lifecycle, independent of the DOM and model.
(function (root, factory) {
  'use strict';
  const api = factory();
  if (root) root.SQPolling = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis, function () {
  'use strict';
  // Completion-driven browser polling. One request owns the controller at a
  // time; cancellation invalidates its generation, and a replacement waits for
  // that ownership to settle even if a fetch implementation ignores abort.
  function create(options) {
    const opts = options || {};
    const request = typeof opts.request === 'function' ? opts.request : () => Promise.reject(new Error('request unavailable'));
    const onData = typeof opts.onData === 'function' ? opts.onData : () => {};
    const onError = typeof opts.onError === 'function' ? opts.onError : () => {};
    const setTimer = opts.setTimeout || setTimeout;
    const clearTimer = opts.clearTimeout || clearTimeout;
    const AbortCtor = opts.AbortController || (typeof AbortController !== 'undefined' ? AbortController : null);
    let intervalMs = Math.max(1, Number(opts.intervalMs) || 2000);
    const timeoutMs = Math.max(1, Number(opts.timeoutMs) || 15000);
    let paused = opts.paused === true;
    let hidden = opts.hidden === true;
    let stopped = true;
    let scheduled = null;
    let active = null;
    let generation = 0;
    let followup = null;
    let forcedSnapshotPending = false;

    const canPoll = () => !stopped && !paused && !hidden;
    function clearScheduled() {
      if (scheduled != null) { clearTimer(scheduled); scheduled = null; }
    }
    function schedule() {
      clearScheduled();
      if (!canPoll() || active) return;
      scheduled = setTimer(() => { scheduled = null; poll(); }, intervalMs);
    }
    function runFollowup(mode) {
      if (mode === 'forced') {
        if (!stopped && !hidden && forcedSnapshotPending) poll(true);
        return;
      }
      if (!canPoll()) return;
      if (mode === 'immediate') poll();
      else schedule();
    }
    function queueFollowup(next) {
      if (next == null) { followup = null; return; }
      if (followup === 'forced') return;
      if (next === 'forced' || next === 'immediate' || followup == null) followup = next;
    }
    function cancelActive(reason, next) {
      generation++;
      clearScheduled();
      if (active) {
        active.cancelReason = reason;
        queueFollowup(next);
        try { active.controller.abort(); } catch {}
      } else {
        runFollowup(next);
      }
    }
    function poll(forceSnapshot) {
      if (stopped || hidden || (paused && !forceSnapshot)) return Promise.resolve(null);
      clearScheduled();
      if (active) {
        queueFollowup(forceSnapshot && forcedSnapshotPending ? 'forced' : 'immediate');
        return active.promise;
      }
      const controller = AbortCtor ? new AbortCtor() : {signal:undefined, abort() {}};
      const record = {generation:++generation, controller, cancelReason:null, timeout:null, promise:null};
      record.timeout = setTimer(() => {
        try { controller.abort(); } catch {}
      }, timeoutMs);
      record.promise = (async () => {
        try {
          const data = await request(controller.signal);
          if (record.generation !== generation || record.cancelReason || stopped) return null;
          await onData(data);
          if (forceSnapshot) forcedSnapshotPending = false;
          return data;
        } catch (error) {
          if (record.generation === generation && !record.cancelReason && !stopped)
            await onError(error);
          return null;
        } finally {
          clearTimer(record.timeout);
          if (active === record) active = null;
          const next = followup;
          followup = null;
          runFollowup(next);
        }
      })();
      active = record;
      return record.promise;
    }
    return {
      start(snapshotWhenPaused) {
        if (!stopped) return active?.promise || null;
        stopped = false;
        forcedSnapshotPending = paused && snapshotWhenPaused === true;
        return !hidden && (!paused || snapshotWhenPaused === true) ? poll(snapshotWhenPaused === true) : null;
      },
      refresh() { if (!canPoll()) return active?.promise || null; return poll(); },
      setPaused(value) {
        const next = value === true;
        if (paused === next) return;
        paused = next;
        if (!paused) {
          forcedSnapshotPending = false;
          if (followup === 'forced') followup = null;
        }
        cancelActive('pause', next ? null : 'immediate');
      },
      setHidden(value) {
        const next = value === true;
        if (hidden === next) return;
        hidden = next;
        if (!hidden && forcedSnapshotPending) {
          return poll(true);
        }
        cancelActive('visibility', next ? null : 'immediate');
      },
      setInterval(value) {
        intervalMs = Math.max(1, Number(value) || intervalMs);
        cancelActive('reconfigure', forcedSnapshotPending ? 'forced' : 'scheduled');
      },
      stop() {
        if (stopped) return;
        stopped = true;
        forcedSnapshotPending = false;
        followup = null;
        cancelActive('stop', null);
      },
      status() { return {active:!!active, paused, hidden, stopped, generation, scheduled:scheduled != null}; }
    };
  }
  return {create};
});
