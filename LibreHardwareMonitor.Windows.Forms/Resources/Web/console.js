// SQ Telemetry Console — pure model layer. Consumes the unchanged data.json.
(function () {
  const SQ = {};
  SQ.RANK = { crit: 3, warn: 2, ok: 1, info: 0, off: -1 };

  SQ.classOf = function (sid) {
    if (!sid) return 'other';
    if (sid.startsWith('/amdcpu') || sid.startsWith('/intelcpu')) return 'cpu';
    if (sid.startsWith('/gpu-nvidia')) return 'gpu';
    if (sid.startsWith('/gpu-amd') || sid.startsWith('/gpu-intel')) return 'igpu';
    if (sid.startsWith('/ram') || sid.startsWith('/vram')) return 'mem';
    if (sid.startsWith('/memory/dimm')) return 'dimm';
    if (sid.startsWith('/nvme') || sid.startsWith('/hdd')) return 'nvme';
    if (sid.startsWith('/usb')) return 'disk';
    if (sid.startsWith('/lpc')) return 'mb';
    if (sid.startsWith('/nic')) return 'nic';
    return 'other';
  };

  SQ.flatten = function (root) {
    const out = [];
    (function walk(node, hw, hwid) {
      if (node.HardwareId !== undefined) { hw = node.Text; hwid = node.HardwareId; }
      if (node.SensorId !== undefined) {
        out.push({ hw, hwid, cls: SQ.classOf(node.SensorId), type: node.Type, text: node.Text,
          value: node.Value, min: node.Min, max: node.Max, raw: node.RawValue, id: node.SensorId });
      }
      (node.Children || []).forEach(c => walk(c, hw, hwid));
    })(root, root.Text, undefined);
    return out;
  };

  SQ.isLimitSensor = function (s) {
    const t = (s.text || '').toLowerCase();
    return t.includes('limit') || t.includes('warning temperature') ||
           t.includes('critical temperature') || t.includes('resolution');
  };

  SQ.deriveLimits = function (sensors) {
    const m = {};
    sensors.forEach(s => {
      if (s.type !== 'Temperature' || s.raw == null) return;
      const t = (s.text || '').toLowerCase();
      m[s.hwid] = m[s.hwid] || {};
      if (t.includes('critical') && (t.includes('high') || t.includes('temperature'))) m[s.hwid].crit = s.raw;
      else if (t.includes('warning') || (t.includes('high') && t.includes('limit'))) m[s.hwid].warn = s.raw;
    });
    return m;
  };

  const TEMPBANDS = { cpu: [85, 95], gpu: [83, 92], igpu: [83, 92], nvme: [70, 80], dimm: [55, 85], mb: null, mem: null };
  function tempStatus(s, limits) {
    const t = (s.text || '').toLowerCase(), lim = limits[s.hwid];
    let warn, crit;
    if (s.cls === 'gpu' && (t.includes('junction') || t.includes('hot'))) { warn = 95; crit = 105; }
    else if ((s.cls === 'nvme' || s.cls === 'dimm') && lim) { warn = lim.warn ?? TEMPBANDS[s.cls][0]; crit = lim.crit ?? TEMPBANDS[s.cls][1]; }
    else { const b = TEMPBANDS[s.cls]; if (!b) return 'info'; warn = b[0]; crit = b[1]; }
    if (s.raw >= crit) return 'crit';
    if (s.raw >= warn) return 'warn';
    return 'ok';
  }
  SQ.statusOf = function (s, limits) {
    if (s.raw == null) return 'off';
    if (s.type === 'Temperature') { if (SQ.isLimitSensor(s)) return 'info'; return tempStatus(s, limits); }
    if (s.type === 'Level' && (s.text || '').toLowerCase().includes('life')) {
      if (s.raw < 5) return 'crit'; if (s.raw < 20) return 'warn'; return 'ok';
    }
    return 'info';
  };

  SQ.splitValue = function (v) {
    if (v == null) return { n: '—', unit: '' };
    const m = String(v).match(/^([\-\d.,]+)\s*(.*)$/);
    return m ? { n: m[1], unit: m[2] } : { n: String(v), unit: '' };
  };

  SQ.pickHero = function (sensors, limits) {
    const H = [], find = p => sensors.find(p);
    const add = (s, label, opts) => { if (s) H.push(Object.assign({ s, label, status: SQ.statusOf(s, limits) }, opts || {})); };
    if (sensors.some(s => s.cls === 'cpu')) {
      const c = sensors.filter(s => s.cls === 'cpu');
      add(c.find(s => s.type === 'Temperature' && s.text.includes('Tctl')), 'CPU Temp', { bounded: [30, 95], unit: '°C' });
      add(c.find(s => s.type === 'Load' && /CPU Total/i.test(s.text)), 'CPU Load', { bounded: [0, 100], unit: '%' });
      add(c.find(s => s.type === 'Power' && /^Package/i.test(s.text)), 'CPU Power', { unit: 'W' });
    }
    if (sensors.some(s => s.cls === 'gpu')) {
      const g = sensors.filter(s => s.cls === 'gpu');
      add(g.find(s => s.type === 'Temperature' && /^GPU Core/i.test(s.text)), 'GPU Temp', { bounded: [25, 92], unit: '°C' });
      add(g.find(s => s.type === 'Temperature' && /Junction/i.test(s.text)), 'GPU Mem Jct', { bounded: [25, 105], unit: '°C' });
      add(g.find(s => s.type === 'Load' && /^GPU Core/i.test(s.text)), 'GPU Load', { bounded: [0, 100], unit: '%' });
      add(g.find(s => s.type === 'Power' && /Package/i.test(s.text)), 'GPU Power', { unit: 'W' });
    }
    add(find(s => s.cls === 'mem' && s.hw === 'Total Memory' && s.type === 'Load'), 'RAM Used', { bounded: [0, 100], unit: '%' });
    const drives = sensors.filter(s => s.cls === 'nvme' && s.type === 'Temperature' && !SQ.isLimitSensor(s) && s.raw != null).sort((a, b) => b.raw - a.raw);
    add(drives[0], 'Drive Temp', { bounded: [25, 80], unit: '°C' });
    return H.slice(0, 9);
  };

  window.SQ = SQ;
  if (!window.SQ_NO_BOOT) { /* Task 3 installs the bootstrap here */ }
})();
