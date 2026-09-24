import { useCallback, useEffect, useState } from 'react';
import { api } from '../api/client';
import type { ZigbeeThreshold, ZigbeeThresholdPage, ZigbeeThresholdResult } from '../api/types';

/**
 * 온·습도 판정 기준 설정(관리자 전용).
 *
 * 좁은 쪽이 이긴다 — 센서 → 사업장 → 전체 기본 → 설정 파일. 사업장이 늘어나면 그 사업장 기준 한 줄로
 * 그 안의 센서가 전부 따라오고, 유독 다른 센서만 따로 잡아 줄 수 있다.
 * 지우면 그 대상은 다시 위 단계를 따른다 — 원래 값으로 되돌리는 방법이다.
 */

type Scope = 'global' | 'site' | 'device';

const SCOPE_LABEL: Record<Scope, string> = {
  global: '전체 기본',
  site: '사업장',
  device: '센서',
};

/** 화면이 들고 있는 편집 중인 값. 숫자 칸은 지우는 중간 상태가 있어 문자열로 둔다. */
type Draft = Record<keyof NumberFields, string>;
type NumberFields = Pick<ZigbeeThreshold,
  'tempNormalMin' | 'tempNormalMax' | 'tempWarnMin' | 'tempWarnMax' |
  'humidNormalMin' | 'humidNormalMax' | 'humidWarnMin' | 'humidWarnMax' |
  'offlineAfterMinutes' | 'lowBatteryPercent' | 'snapshotIntervalMinutes'>;

const FIELDS: (keyof NumberFields)[] = [
  'tempNormalMin', 'tempNormalMax', 'tempWarnMin', 'tempWarnMax',
  'humidNormalMin', 'humidNormalMax', 'humidWarnMin', 'humidWarnMax',
  'offlineAfterMinutes', 'lowBatteryPercent', 'snapshotIntervalMinutes',
];
const INT_FIELDS = new Set<string>(['offlineAfterMinutes', 'lowBatteryPercent', 'snapshotIntervalMinutes']);

function toDraft(t: ZigbeeThreshold): Draft {
  const d = {} as Draft;
  for (const f of FIELDS) d[f] = String(t[f]);
  return d;
}

export default function TempHumidityLimits({ onClose, onSaved }: { onClose: () => void; onSaved: () => void }) {
  const [page, setPage] = useState<ZigbeeThresholdPage | null>(null);
  const [scope, setScope] = useState<Scope>('global');
  const [key, setKey] = useState('');
  const [draft, setDraft] = useState<Draft | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const d = await api.get<ZigbeeThresholdPage>('/api/iot/zigbee/thresholds');
    setPage(d);
    return d;
  }, []);

  useEffect(() => { void load().catch(() => setPage(null)); }, [load]);

  /** 고른 대상에 이미 저장된 기준이 있으면 그것을, 없으면 지금 적용 중인 기본값을 출발점으로 준다. */
  useEffect(() => {
    if (!page) return;
    const stored = page.rows.find(r => r.scope === scope && (scope === 'global' || r.scopeKey === key));
    setDraft(toDraft(stored ?? page.default));
  }, [page, scope, key]);

  const current = page?.rows.find(r => r.scope === scope && (scope === 'global' || r.scopeKey === key));
  const options = page ? (scope === 'site' ? page.sites : scope === 'device' ? page.devices : []) : [];

  async function save() {
    if (!draft || busy) return;
    const body: Record<string, unknown> = { scope, scopeKey: scope === 'global' ? '' : key };
    for (const f of FIELDS) {
      const n = Number(draft[f]);
      if (draft[f].trim() === '' || Number.isNaN(n)) { alert('빈 칸이나 숫자가 아닌 값이 있습니다.'); return; }
      // 분·%·주기 칸은 서버가 정수로만 받는다. 소수를 보내면 "요청 실패 (400)" 만 떠서 원인을 알 수 없었다.
      if (INT_FIELDS.has(f) && !Number.isInteger(n)) { alert('미수신 판정·배터리·기록 주기는 정수로 넣어 주세요.'); return; }
      body[f] = n;
    }
    if (scope !== 'global' && !key) { alert(`적용할 ${SCOPE_LABEL[scope]}을(를) 고르세요.`); return; }

    setBusy(true);
    try {
      const r = await api.put<ZigbeeThresholdResult>('/api/iot/zigbee/thresholds', body);
      alert(r.message);
      if (r.success) { await load(); onSaved(); }
    } catch (err) {
      alert(err instanceof Error ? err.message : '저장에 실패했습니다.');
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!current || busy) return;
    if (!confirm(`'${current.label}' 기준을 지울까요? 지우면 상위 기준을 따릅니다.`)) return;
    setBusy(true);
    try {
      const q = scope === 'global' ? '' : `?key=${encodeURIComponent(key)}`;
      const r = await api.del<ZigbeeThresholdResult>(`/api/iot/zigbee/thresholds/${scope}${q}`);
      alert(r.message);
      if (r.success) { await load(); onSaved(); }
    } catch (err) {
      alert(err instanceof Error ? err.message : '삭제에 실패했습니다.');
    } finally {
      setBusy(false);
    }
  }

  function set(field: keyof NumberFields, value: string) {
    setDraft(d => (d ? { ...d, [field]: value } : d));
  }

  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget && !busy) onClose(); }}>
      <div className="modal-box th-limits">
        <h3>온·습도 기준 설정</h3>
        <p className="vd-hint">
          좁은 쪽이 이깁니다 — 센서 → 사업장 → 전체 기본. 사업장 기준을 두면 그 안의 센서가 모두 따르고,
          한 대만 다르게 두려면 그 센서에 따로 지정하세요.
        </p>

        {!page && <div className="th-empty sm">기준을 불러오는 중…</div>}

        {page && (
          <>
            <div className="th-lim-scope">
              {(['global', 'site', 'device'] as Scope[]).map(s => (
                <button key={s} type="button" className={`th-pick ${scope === s ? 'on' : ''}`}
                        onClick={() => { setScope(s); setKey(''); }}>{SCOPE_LABEL[s]}</button>
              ))}
              {scope !== 'global' && (
                <select className="input th-lim-key" value={key} onChange={e => setKey(e.target.value)}>
                  <option value="">{SCOPE_LABEL[scope]} 고르기</option>
                  {options.map(o => <option key={o.key} value={o.key}>{o.label}</option>)}
                </select>
              )}
            </div>

            {draft && (scope === 'global' || key) && (
              <>
                <div className="th-lim-state">
                  {current
                    ? <>이 대상에 <b>따로 지정된 기준</b>이 있습니다. {current.updatedBy && <span className="th-dim">({current.updatedBy} 수정)</span>}</>
                    : <>아직 따로 지정하지 않았습니다. 지금은 상위 기준을 따르며, 저장하면 이 대상에만 적용됩니다.</>}
                </div>

                <Band title="온도 (℃)" draft={draft} set={set}
                      keys={['tempWarnMin', 'tempNormalMin', 'tempNormalMax', 'tempWarnMax']} />
                <Band title="습도 (%)" draft={draft} set={set}
                      keys={['humidWarnMin', 'humidNormalMin', 'humidNormalMax', 'humidWarnMax']} />

                <div className="th-lim-row">
                  <label>미수신 판정 (분)<input className="input" value={draft.offlineAfterMinutes}
                    onChange={e => set('offlineAfterMinutes', e.target.value)} /></label>
                  <label>배터리 부족 (%)<input className="input" value={draft.lowBatteryPercent}
                    onChange={e => set('lowBatteryPercent', e.target.value)} /></label>
                </div>
                <p className="vd-hint">
                  미수신 판정은 센서 보고 주기보다 길어야 합니다. 값이 안 변하면 센서는 한참 뒤에야
                  보고하므로, 너무 짧게 잡으면 멀쩡한 센서가 계속 '통신 끊김' 으로 뜹니다.
                </p>

                {scope === 'global' && (
                  <div className="th-lim-band">
                    <div className="th-lim-band-title">이력 기록 주기 (전체 공통)</div>
                    <div className="th-lim-row">
                      <label>몇 분마다 기록<input className="input" value={draft.snapshotIntervalMinutes}
                        onChange={e => set('snapshotIntervalMinutes', e.target.value)} /></label>
                    </div>
                    <p className="vd-hint">
                      센서가 조용해도 마지막으로 받은 값을 이 간격마다 이력에 남깁니다. 그래프가 끊기지 않고
                      배터리에는 영향이 없지만, 그 값은 <b>그때 새로 잰 값이 아니라 마지막 측정값의 반복</b>입니다.
                      0 이면 끄고 실제 수신만 남깁니다. 아래 '최근 수신 이력' 표에는 실제 수신만 나옵니다.
                    </p>
                  </div>
                )}
              </>
            )}

            {scope !== 'global' && !key && (
              <div className="th-empty sm">위에서 {SCOPE_LABEL[scope]}을(를) 고르세요.</div>
            )}
          </>
        )}

        <div className="modal-actions">
          {current && <button type="button" className="btn btn-ghost th-lim-del" disabled={busy} onClick={remove}>기준 지우기</button>}
          <button type="button" className="btn btn-ghost" disabled={busy} onClick={onClose}>닫기</button>
          <button type="button" className="btn btn-primary" disabled={busy || !draft || (scope !== 'global' && !key)} onClick={save}>
            {busy ? '저장 중…' : '저장'}
          </button>
        </div>
      </div>
    </div>
  );
}

/** 경고 하한 · 정상 하한 · 정상 상한 · 경고 상한을 한 줄로 — 순서대로 읽으면 구간이 그려진다. */
function Band({ title, draft, set, keys }: {
  title: string; draft: Draft;
  set: (f: keyof NumberFields, v: string) => void;
  keys: [keyof NumberFields, keyof NumberFields, keyof NumberFields, keyof NumberFields];
}) {
  const [warnLo, normLo, normHi, warnHi] = keys;
  return (
    <div className="th-lim-band">
      <div className="th-lim-band-title">{title}</div>
      <div className="th-lim-row">
        <label>경고 하한<input className="input" value={draft[warnLo]} onChange={e => set(warnLo, e.target.value)} /></label>
        <label>정상 하한<input className="input" value={draft[normLo]} onChange={e => set(normLo, e.target.value)} /></label>
        <label>정상 상한<input className="input" value={draft[normHi]} onChange={e => set(normHi, e.target.value)} /></label>
        <label>경고 상한<input className="input" value={draft[warnHi]} onChange={e => set(warnHi, e.target.value)} /></label>
      </div>
      <p className="vd-hint">
        정상 하한~정상 상한이 <b>정상</b>, 그 바깥에서 경고 하한~경고 상한 사이가 <b>주의</b>, 더 벗어나면 <b>경고</b>입니다.
      </p>
    </div>
  );
}
