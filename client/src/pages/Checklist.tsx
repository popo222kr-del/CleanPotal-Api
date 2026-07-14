import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import type { Zone, InspectionItem, InspectionRecord } from '../api/types';
import './Checklist.css';

// 현재 교대 판별: 07:00~17:30 주간, 그 외 야간
function currentShift(): string {
  const h = new Date().getHours() + new Date().getMinutes() / 60;
  return h >= 7 && h < 17.5 ? '주간' : '야간';
}
const todayStr = () => {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
};

export default function Checklist() {
  const [zones, setZones] = useState<Zone[]>([]);
  const [zone, setZone] = useState<string>('');
  const [items, setItems] = useState<InspectionItem[]>([]);
  const [checked, setChecked] = useState<Set<number>>(new Set());
  const [records, setRecords] = useState<InspectionRecord[]>([]);
  const [shift] = useState(currentShift());

  useEffect(() => {
    api.get<Zone[]>('/api/checklist/zones').then(z => { setZones(z); if (z[0]) setZone(z[0].key); });
  }, []);

  const loadZone = useCallback(async () => {
    if (!zone) return;
    setItems(await api.get<InspectionItem[]>(`/api/checklist/items?zone=${zone}`));
    setChecked(new Set());
    setRecords(await api.get<InspectionRecord[]>(`/api/checklist/records?date=${todayStr()}`));
  }, [zone]);
  useEffect(() => { loadZone(); }, [loadZone]);

  // 현재 구역+교대 제출 여부
  const submitted = records.find(r => r.zone === zone && r.shift === shift);

  function toggle(id: number) {
    setChecked(prev => {
      const s = new Set(prev);
      s.has(id) ? s.delete(id) : s.add(id);
      return s;
    });
  }
  async function submit() {
    if (checked.size < items.length) {
      if (!confirm(`${items.length}개 중 ${checked.size}개만 체크되었습니다. 그대로 제출할까요?`)) return;
    }
    await api.post('/api/checklist/submit', {
      zone, date: todayStr(), shift, checkedCount: checked.size, totalCount: items.length,
    });
    loadZone();
    alert('제출되었습니다.');
  }

  const allChecked = items.length > 0 && checked.size === items.length;
  const zonesByGroup = zones.reduce<Record<string, Zone[]>>((acc, z) => {
    (acc[z.group] ??= []).push(z); return acc;
  }, {});

  return (
    <div className="cl-wrap">
      <header className="cl-header">
        <h2>세정 현장체크</h2>
        <div className="cl-meta">
          {todayStr()} · <span className={`cl-shift ${shift === '주간' ? 'day' : 'night'}`}>{shift}</span>
        </div>
      </header>

      <div className="cl-zones">
        {Object.entries(zonesByGroup).map(([group, zs]) => (
          <div key={group} className="cl-zgroup">
            <div className="cl-glabel">{group}</div>
            <div className="cl-zbtns">
              {zs.map(z => {
                const done = records.some(r => r.zone === z.key);
                return (
                  <button key={z.key} className={`cl-zbtn ${zone === z.key ? 'active' : ''} ${done ? 'done' : ''}`} onClick={() => setZone(z.key)}>
                    {z.label}{done && ' ✓'}
                  </button>
                );
              })}
            </div>
          </div>
        ))}
      </div>

      {submitted && (
        <div className="cl-submitted-banner">
          ✅ {shift} 제출 완료 — {submitted.worker} ({new Date(submitted.submittedAt).toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' })}) · {submitted.checkedCount}/{submitted.totalCount}
        </div>
      )}

      <div className="cl-items">
        {items.map((it, i) => (
          <label key={it.id} className={`cl-item ${checked.has(it.id) ? 'on' : ''}`}>
            <span className="cl-num">{i + 1}</span>
            <span className="cl-text">{it.text}</span>
            <input type="checkbox" checked={checked.has(it.id)} onChange={() => toggle(it.id)} />
            <span className="cl-box" />
          </label>
        ))}
        {items.length === 0 && <div className="cl-empty">점검 항목이 없습니다</div>}
      </div>

      <div className="cl-footer">
        <div className="cl-progress">{checked.size} / {items.length} 완료</div>
        <button className={`cl-submit ${allChecked ? 'ready' : ''}`} onClick={submit} disabled={items.length === 0}>
          {submitted ? '재제출' : '제출'}
        </button>
      </div>
    </div>
  );
}
