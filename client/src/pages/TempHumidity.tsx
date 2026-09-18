import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import { useAccess } from '../auth/useAccess';
import { useIsMobile } from '../hooks/useIsMobile';
import TempHumidityLimits from './TempHumidityLimits';
import { exportTempHumidity } from './tempHumidityExcel';
import type {
  SensorExport, SensorHistory, SensorReading, SensorSnapshot, SensorStatusCode,
  SensorSummaryPage, ZigbeeStatus,
} from '../api/types';
import './TempHumidity.css';

/**
 * 현장 점검 — 동탄 물류창고 온·습도 모니터링.
 *
 * 값과 상태 판정은 서버가 준다(/api/iot/zigbee/...). 화면은 브리지 주소도, 판정 기준도 모른다 —
 * 창고 PC 주소가 바뀌거나 기준이 바뀌어도 이 파일은 그대로다.
 *
 * 갱신은 지금은 주기 조회다. 나중에 WebSocket/SSE 로 바꿔도 되도록 불러오는 일은 loadLatest 하나에 모아 두었다.
 */

/** 화면 갱신 주기. 센서가 1~2분에 한 번 올리므로 이보다 잦게 볼 이유는 없다. */
const REFRESH_MS = 10_000;
/** 아래 표에 보여 줄 최근 수신 줄 수. */
const RECENT_LIMIT = 50;

/** 조회 구간. 'custom' 은 날짜를 직접 넣는다. */
type RangeKey = '24h' | '7d' | '30d' | 'custom';
const RANGE_HOURS: Record<Exclude<RangeKey, 'custom'>, number> = { '24h': 24, '7d': 168, '30d': 720 };
const RANGE_LABEL: Record<RangeKey, string> = {
  '24h': '24시간', '7d': '7일', '30d': '30일', custom: '기간 지정',
};

/** 분을 사람이 읽는 길이로 — 기준을 벗어난 시간을 보여 줄 때 쓴다. */
function duration(minutes: number) {
  if (minutes <= 0) return '-';
  if (minutes < 60) return `${minutes}분`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  if (h < 24) return m > 0 ? `${h}시간 ${m}분` : `${h}시간`;
  return `${Math.floor(h / 24)}일 ${h % 24}시간`;
}

/** datetime-local 입력값이 비어 있지 않은지. 둘 다 있어야 조회한다. */
function customReady(from: string, to: string) {
  return from.length > 0 && to.length > 0 && from <= to;
}

/**
 * 지금 그리고 있는 게 원본인지 묶은 것인지 한 줄로 알려 준다.
 * 긴 구간은 서버가 알아서 묶는데, 그걸 모르면 "왜 점이 듬성듬성하지" 하고 오해한다.
 */
function rangeNote(histories: SensorHistory[]): string {
  const h = histories[0];
  if (!h) return '';
  const parts: string[] = [];
  if (h.bucketMinutes > 0) parts.push(`${h.bucketMinutes}분 평균`);
  if (h.realOnly) parts.push('실제 수신만');
  return parts.length > 0 ? `(${parts.join(' · ')})` : '';
}

const STATUS_ORDER: SensorStatusCode[] = ['alert', 'warn', 'offline', 'normal'];

function fmt(v: number | null, unit: string, digits = 1) {
  return v === null || Number.isNaN(v) ? '--' : `${v.toFixed(digits)}${unit}`;
}

/** "14:26:02" — 날짜가 오늘이 아니면 날짜도 같이 보여 준다. */
function clock(iso: string | null) {
  if (!iso) return '--';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '--';
  const hhmmss = d.toLocaleTimeString('ko-KR', { hour12: false });
  const today = new Date();
  const sameDay = d.getFullYear() === today.getFullYear() && d.getMonth() === today.getMonth() && d.getDate() === today.getDate();
  return sameDay ? hhmmss : `${d.getMonth() + 1}/${d.getDate()} ${hhmmss}`;
}

/** 통신 품질(LQI)은 숫자만 보면 감이 안 와서 한 마디를 붙인다. */
function linkText(lqi: number | null) {
  if (lqi === null) return '--';
  const grade = lqi >= 150 ? '양호' : lqi >= 80 ? '보통' : '약함';
  return `${lqi} (${grade})`;
}

export default function TempHumidity() {
  const isMobile = useIsMobile();
  const { isAdmin } = useAccess();
  // 기준 설정은 관리자만 연다 — 여기 숫자가 바뀌면 모든 사람의 경고가 같이 바뀐다.
  const [limitsOpen, setLimitsOpen] = useState(false);
  const [sensors, setSensors] = useState<SensorReading[]>([]);
  const [status, setStatus] = useState<ZigbeeStatus | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [pick, setPick] = useState<string>('all');          // 그래프에 볼 센서
  const [histories, setHistories] = useState<SensorHistory[]>([]);

  // ── 조회 구간 ──
  const [range, setRange] = useState<RangeKey>('24h');
  const [customFrom, setCustomFrom] = useState('');
  const [customTo, setCustomTo] = useState('');
  const [summary, setSummary] = useState<SensorSummaryPage | null>(null);
  const [busy, setBusy] = useState(false);
  /** 크게 보기로 연 그래프. null 이면 닫힌 상태다. */
  const [zoom, setZoom] = useState<null | 'temperature' | 'humidity'>(null);

  // 최근 수신 이력. 서버 표에서 읽으므로 새로 고쳐도 목록이 비지 않는다.
  const [recent, setRecent] = useState<SensorReading[]>([]);

  /** 화면 한 판을 채우는 조회. 나중에 WebSocket 으로 바꾸면 이 함수만 갈아끼우면 된다. */
  const loadLatest = useCallback(async () => {
    try {
      const [snap, rows] = await Promise.all([
        api.get<SensorSnapshot>('/api/iot/zigbee/latest'),
        api.get<SensorReading[]>(`/api/iot/zigbee/recent?limit=${RECENT_LIMIT}`),
      ]);
      setSensors(snap.sensors);
      setStatus(snap.status);
      setRecent(rows);
    } catch {
      // 포털 자체가 답하지 않는 경우다. 화면을 지우지 않고 마지막 값을 그대로 두되 계통은 끊김으로 표시한다.
      setStatus(s => s ? { ...s, mqttOnline: false, message: '서버에 연결하지 못했습니다.' } : s);
    } finally {
      setLoaded(true);
    }
  }, []);

  useEffect(() => {
    void loadLatest();
    const t = setInterval(() => { void loadLatest(); }, REFRESH_MS);
    return () => clearInterval(t);
  }, [loadLatest]);

  // 조회 구간을 주소 조각 하나로 — 모든 조회(그래프·요약·내보내기)가 같은 구간을 본다.
  const query = useMemo(() => {
    if (range !== 'custom') return `hours=${RANGE_HOURS[range]}`;
    return customReady(customFrom, customTo)
      ? `from=${encodeURIComponent(customFrom)}&to=${encodeURIComponent(customTo)}`
      : '';
  }, [range, customFrom, customTo]);

  // 지난 구간을 보고 있을 때는 자동 갱신하지 않는다 — 보고 있는 화면이 저절로 바뀌면 안 된다.
  const isLive = range === '24h';

  const deviceIds = useMemo(() => sensors.map(s => s.deviceId).join(','), [sensors]);
  const loadHistory = useCallback(async () => {
    const ids = deviceIds ? deviceIds.split(',') : [];
    if (ids.length === 0 || query === '') return;
    const rows = await Promise.all(ids.map(id =>
      api.get<SensorHistory>(`/api/iot/zigbee/history/${encodeURIComponent(id)}?${query}`)
        .catch(() => null)));
    setHistories(rows.filter((r): r is SensorHistory => r !== null));
  }, [deviceIds, query]);

  const loadSummary = useCallback(async () => {
    if (query === '') { setSummary(null); return; }
    try { setSummary(await api.get<SensorSummaryPage>(`/api/iot/zigbee/summary?${query}`)); }
    catch { setSummary(null); }
  }, [query]);

  useEffect(() => {
    void loadHistory();
    void loadSummary();
    if (!isLive) return;
    const t = setInterval(() => { void loadHistory(); void loadSummary(); }, REFRESH_MS * 6);
    return () => clearInterval(t);
  }, [loadHistory, loadSummary, isLive]);

  async function download() {
    if (busy || query === '') return;
    setBusy(true);
    try {
      const data = await api.get<SensorExport>(`/api/iot/zigbee/export?${query}`);
      if (data.rows.length === 0) { alert('내보낼 값이 없습니다.'); return; }
      await exportTempHumidity(data);
      if (data.truncated) {
        alert(`줄이 너무 많아 앞에서부터 ${data.rows.length.toLocaleString()}건만 내보냈습니다.\n구간을 나눠서 다시 받으세요.`);
      }
    } catch (err) {
      alert(err instanceof Error ? err.message : '내보내기에 실패했습니다.');
    } finally {
      setBusy(false);
    }
  }

  const shownHistories = useMemo(
    () => (pick === 'all' ? histories : histories.filter(h => h.deviceId === pick)),
    [histories, pick]);

  // 가장 나쁜 상태를 머리말 옆에 한 줄로 — 카드를 다 읽지 않아도 알 수 있게.
  const worst = useMemo(() => {
    for (const s of STATUS_ORDER) if (sensors.some(x => x.status === s)) return s;
    return 'normal' as SensorStatusCode;
  }, [sensors]);

  return (
    <div className="th-page">
      <header className="pg-header">
        <div><h2>동탄 물류창고 온·습도 모니터링</h2></div>
        <SystemStatus status={status} />
        {isAdmin && <button className="btn btn-ghost" onClick={() => setLimitsOpen(true)}>기준 설정</button>}
      </header>

      <div className="pg-body">
        {!loaded && <div className="th-empty">불러오는 중…</div>}

        {loaded && sensors.length === 0 && (
          <div className="th-empty">등록된 센서가 없습니다. 서버 설정(Zigbee:Sensors)을 확인하세요.</div>
        )}

        {sensors.length > 0 && (
          <>
            <div className={`th-cards ${isMobile ? 'mobile' : ''}`}>
              {sensors.map(s => <SensorCard key={s.deviceId} s={s} />)}
            </div>

            <section className="th-sec">
              <div className="th-sec-head">
                <RangeBar range={range} onChange={setRange} />
                <button className="btn btn-ghost th-xls" disabled={busy || query === ''} onClick={download}>
                  {busy ? '만드는 중…' : '엑셀 내보내기'}
                </button>
              </div>

              {range === 'custom' && (
                <CustomRange from={customFrom} to={customTo} onFrom={setCustomFrom} onTo={setCustomTo} />
              )}

              <div className="th-sec-head th-sub">
                <SensorBar sensors={sensors} pick={pick} onChange={setPick} />
                <span className="th-dim">{rangeNote(histories)}</span>
              </div>

              <div className="th-charts">
                <TrendChart title="온도" unit="℃" histories={shownHistories} field="temperature"
                            onOpen={() => setZoom('temperature')} />
                <TrendChart title="습도" unit="%" histories={shownHistories} field="humidity"
                            onOpen={() => setZoom('humidity')} />
              </div>
            </section>

            {summary && summary.sensors.length > 0 && (
              <section className="th-sec">
                <div className="th-sec-head"><b>구간 요약</b><span className="th-dim">{RANGE_LABEL[range]}</span></div>
                <SummaryTable page={summary} />
              </section>
            )}

            <section className="th-sec">
              <div className="th-sec-head"><b>최근 수신 이력</b><span className="th-dim">{recent.length}건</span></div>
              <RecentTable rows={recent} worst={worst} />
            </section>
          </>
        )}
      </div>

      {zoom && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setZoom(null); }}>
          <div className="modal-box th-zoom-box">
            {/* 크게 본 채로 기간과 센서를 바꾼다 — 닫았다 다시 여는 것이 제일 성가시다 */}
            <div className="th-sec-head">
              <div className="th-ranges">
                <button className={`th-pick ${zoom === 'temperature' ? 'on' : ''}`} onClick={() => setZoom('temperature')}>온도</button>
                <button className={`th-pick ${zoom === 'humidity' ? 'on' : ''}`} onClick={() => setZoom('humidity')}>습도</button>
              </div>
              <RangeBar range={range} onChange={setRange} />
              <span className="th-dim">{rangeNote(histories)}</span>
            </div>

            {range === 'custom' && (
              <CustomRange from={customFrom} to={customTo} onFrom={setCustomFrom} onTo={setCustomTo} />
            )}

            <div className="th-sec-head th-sub">
              <SensorBar sensors={sensors} pick={pick} onChange={setPick} />
            </div>

            <TrendChart
              title={zoom === 'temperature' ? '온도' : '습도'}
              unit={zoom === 'temperature' ? '℃' : '%'}
              histories={shownHistories}
              field={zoom}
              big
            />

            <div className="modal-actions">
              <button type="button" className="btn btn-ghost" disabled={busy || query === ''} onClick={download}>
                {busy ? '만드는 중…' : '엑셀 내보내기'}
              </button>
              <button type="button" className="btn btn-primary" onClick={() => setZoom(null)}>닫기</button>
            </div>
          </div>
        </div>
      )}

      {limitsOpen && (
        <TempHumidityLimits
          onClose={() => setLimitsOpen(false)}
          onSaved={() => { void loadLatest(); }}
        />
      )}
    </div>
  );
}

// ── 조회 조건 (화면과 확대 창이 같은 것을 쓴다) ──

function RangeBar({ range, onChange }: { range: RangeKey; onChange: (r: RangeKey) => void }) {
  return (
    <div className="th-ranges">
      {(['24h', '7d', '30d', 'custom'] as RangeKey[]).map(r => (
        <button key={r} className={`th-pick ${range === r ? 'on' : ''}`} onClick={() => onChange(r)}>
          {RANGE_LABEL[r]}
        </button>
      ))}
    </div>
  );
}

function CustomRange({ from, to, onFrom, onTo }: {
  from: string; to: string; onFrom: (v: string) => void; onTo: (v: string) => void;
}) {
  return (
    <div className="th-custom">
      <label>시작<input type="datetime-local" className="input" value={from} onChange={e => onFrom(e.target.value)} /></label>
      <label>종료<input type="datetime-local" className="input" value={to} onChange={e => onTo(e.target.value)} /></label>
      {!customReady(from, to) && <span className="th-dim">시작과 종료를 모두 넣으세요 (최대 92일)</span>}
    </div>
  );
}

function SensorBar({ sensors, pick, onChange }: {
  sensors: SensorReading[]; pick: string; onChange: (v: string) => void;
}) {
  return (
    <div className="th-picks">
      <button className={`th-pick ${pick === 'all' ? 'on' : ''}`} onClick={() => onChange('all')}>전체</button>
      {sensors.map(s => (
        <button key={s.deviceId} className={`th-pick ${pick === s.deviceId ? 'on' : ''}`}
                onClick={() => onChange(s.deviceId)}>{s.deviceName}</button>
      ))}
    </div>
  );
}

// ── 수집 계통 표시 ────────────────────────────────────────────────────────

function SystemStatus({ status }: { status: ZigbeeStatus | null }) {
  if (!status) return null;
  const mqtt = status.mqttOnline;
  const z2m = status.zigbee2mqttOnline;
  const allOn = status.sensorsTotal > 0 && status.sensorsOnline === status.sensorsTotal;

  // 자리를 적게 쓰려고 한 줄로 줄였다 — 점 색이 상태고, 자세한 사정은 마우스를 올리면 나온다.
  return (
    <div className="th-sys">
      <span className={`th-sys-dot ${mqtt ? 'ok' : 'bad'}`}
            title={mqtt ? 'MQTT 브로커에 연결되어 있습니다.' : (status.message ?? 'MQTT 브로커에 연결하지 못했습니다.')}>
        <i /> MQTT
      </span>
      <span className={`th-sys-dot ${z2m === false ? 'bad' : z2m ? 'ok' : 'idle'}`}
            title={z2m === false ? 'Zigbee2MQTT 가 멎었습니다.'
                 : z2m ? 'Zigbee2MQTT 가 동작 중입니다.'
                 : '아직 Zigbee2MQTT 소식을 받지 못했습니다(고장이 아니라 판단 보류).'}>
        <i /> Zigbee2MQTT
      </span>
      <span className={`th-sys-dot ${allOn ? 'ok' : 'bad'}`}
            title={allOn ? '모든 센서에서 값이 들어오고 있습니다.' : '값이 들어오지 않는 센서가 있습니다.'}>
        <i /> 센서 {status.sensorsOnline}/{status.sensorsTotal}
      </span>
    </div>
  );
}

// ── 센서 카드 ─────────────────────────────────────────────────────────────

function SensorCard({ s }: { s: SensorReading }) {
  return (
    <div className={`th-card st-${s.status}`} title={s.statusReason ?? undefined}>
      <div className="th-card-top">
        <span className="th-card-name">{s.deviceName}</span>
        <span className={`th-badge st-${s.status}`}>{s.statusLabel}</span>
      </div>
      <div className="th-readings">
        <div className="th-read">
          <span className="th-read-v">{fmt(s.temperature, '')}</span>
          <span className="th-read-u">℃</span>
        </div>
        <div className="th-read sub">
          <span className="th-read-v">{fmt(s.humidity, '')}</span>
          <span className="th-read-u">%</span>
        </div>
      </div>
      {s.statusReason && <p className="th-reason">{s.statusReason}</p>}
      <dl className="th-meta">
        <div><dt>배터리</dt><dd className={s.batteryLow ? 'warn' : ''}>{s.battery === null ? '--' : `${s.battery}%`}</dd></div>
        <div><dt>통신 품질</dt><dd>{linkText(s.linkQuality)}</dd></div>
        <div><dt>최종 수신</dt><dd>{clock(s.receivedAt)}</dd></div>
        <div><dt>판정 기준</dt><dd className="th-src">{s.limitSource}</dd></div>
      </dl>
    </div>
  );
}

// ── 추이 그래프 (프로젝트의 다른 화면과 같은 inline SVG 방식) ──────────────

const LINE_COLORS = ['#2563EB', '#0EA5E9', '#7C3AED', '#059669', '#D97706', '#DC2626'];

function TrendChart({ title, unit, histories, field, big, onOpen }: {
  title: string; unit: string; histories: SensorHistory[]; field: 'temperature' | 'humidity';
  /** 크게 보기. 글자와 눈금을 그 크기에 맞춰 다시 잡는다. */
  big?: boolean;
  /** 누르면 크게 보기. 크게 본 상태에서는 넘기지 않는다. */
  onOpen?: () => void;
}) {
  // 구간은 서버가 이미 잘라서 준다 — 여기서 또 자르면 '기간 지정' 조회가 비어 버린다.
  const series = histories.map((h, i) => ({
    name: h.deviceName,
    color: LINE_COLORS[i % LINE_COLORS.length],
    pts: h.points
      .map(p => ({ t: new Date(p.receivedAt).getTime(), v: p[field] }))
      .filter(p => !Number.isNaN(p.t) && p.v !== null) as { t: number; v: number }[],
  })).filter(s => s.pts.length > 0);

  if (series.length === 0) {
    return (
      <div className="th-chart-box">
        <div className="th-chart-title">{title} 추이</div>
        <div className="th-empty sm">표시할 이력이 없습니다.</div>
      </div>
    );
  }

  const all = series.flatMap(s => s.pts);
  const tMin = Math.min(...all.map(p => p.t));
  const tMax = Math.max(...all.map(p => p.t));
  const vLo = Math.min(...all.map(p => p.v));
  const vHi = Math.max(...all.map(p => p.v));
  // 값이 거의 안 변해도 선이 바닥에 붙지 않게 위아래로 조금 벌린다.
  const pad = Math.max(1, (vHi - vLo) * 0.2);
  const lo = vLo - pad;
  const hi = vHi + pad;

  // 크게 볼 때는 화면을 꽉 채우므로 여백과 글자를 그 비율에 맞춘다(작은 값 그대로 키우면 글자가 뭉갠다).
  const W = big ? 1400 : 640;
  const H = big ? 560 : 220;
  const padT = big ? 16 : 12;
  const padB = big ? 44 : 30;
  const padL = big ? 62 : 44;
  const padR = big ? 20 : 12;
  const font = big ? 13 : 10;
  const xTicks = big ? [0, 0.2, 0.4, 0.6, 0.8, 1] : [0, 0.5, 1];
  const plotW = W - padL - padR;
  const plotH = H - padT - padB;
  const x = (t: number) => padL + (tMax === tMin ? plotW / 2 : ((t - tMin) / (tMax - tMin)) * plotW);
  const y = (v: number) => padT + plotH * (1 - (v - lo) / (hi - lo));
  const ticks = [0, 0.25, 0.5, 0.75, 1];

  return (
    <div className={`th-chart-box ${big ? 'big' : 'clickable'}`}
         onClick={onOpen}
         title={onOpen ? '누르면 크게 볼 수 있습니다' : undefined}>
      <div className="th-chart-title">{title} 추이</div>
      <div className="th-chart-scroll">
        <svg viewBox={`0 0 ${W} ${H}`} className={`th-chart ${big ? 'big' : ''}`} preserveAspectRatio="none">
          {ticks.map(t => (
            <g key={t}>
              <line x1={padL} x2={W - padR} y1={padT + plotH * t} y2={padT + plotH * t} stroke="#F1F5F9" />
              <text x={padL - 6} y={padT + plotH * t + 4} fontSize={font} fill="#9CA3AF" textAnchor="end">
                {(hi - (hi - lo) * t).toFixed(1)}
              </text>
            </g>
          ))}
          <text x={big ? 16 : 10} y={H / 2} fontSize={font} fill="#6B7280"
                transform={`rotate(-90 ${big ? 16 : 10} ${H / 2})`} textAnchor="middle">{unit}</text>
          {xTicks.map(t => {
            const at = tMin + (tMax - tMin) * t;
            return (
              <text key={t} x={x(at)} y={H - (big ? 16 : 10)} fontSize={font} fill="#64748B"
                    textAnchor={t === 0 ? 'start' : t === 1 ? 'end' : 'middle'}>
                {axisLabel(at, tMax - tMin)}
              </text>
            );
          })}
          {series.map(s => (
            <polyline key={s.name} fill="none" stroke={s.color} strokeWidth={big ? 2.2 : 1.8} strokeLinejoin="round"
                      points={s.pts.map(p => `${x(p.t)},${y(p.v)}`).join(' ')} />
          ))}
        </svg>
      </div>
      <div className="th-legend">
        {series.map(s => <span key={s.name} className="th-leg"><i style={{ background: s.color }} />{s.name}</span>)}
      </div>
    </div>
  );
}

/** 구간이 하루를 넘으면 시각만으로는 어느 날인지 알 수 없다. */
function axisLabel(at: number, spanMs: number) {
  const d = new Date(at);
  const p = (n: number) => String(n).padStart(2, '0');
  return spanMs > 36 * 3600_000
    ? `${d.getMonth() + 1}/${d.getDate()}`
    : `${p(d.getHours())}:${p(d.getMinutes())}`;
}

// ── 구간 요약 ──

function SummaryTable({ page }: { page: SensorSummaryPage }) {
  const num = (v: number | null, unit: string) => (v === null ? '-' : `${v.toFixed(1)}${unit}`);
  return (
    <div className="th-table-wrap">
      <table className="th-table">
        <thead>
          <tr>
            <th>센서</th><th>건수</th>
            <th>온도 최저</th><th>온도 평균</th><th>온도 최고</th>
            <th>습도 최저</th><th>습도 평균</th><th>습도 최고</th>
            <th>주의</th><th>경고</th><th>판정 기준</th>
          </tr>
        </thead>
        <tbody>
          {page.sensors.map(s => (
            <tr key={s.deviceId}>
              <td className="th-td-name">{s.deviceName}</td>
              <td>{s.count.toLocaleString()}</td>
              <td>{num(s.tempMin, '℃')}</td>
              <td>{num(s.tempAvg, '℃')}</td>
              <td>{num(s.tempMax, '℃')}</td>
              <td>{num(s.humidMin, '%')}</td>
              <td>{num(s.humidAvg, '%')}</td>
              <td>{num(s.humidMax, '%')}</td>
              <td className={s.warnMinutes > 0 ? 'th-warn' : ''}>{duration(s.warnMinutes)}</td>
              <td className={s.alertMinutes > 0 ? 'th-alert' : ''}>{duration(s.alertMinutes)}</td>
              <td className="th-src">{s.limitSource}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="vd-hint">
        '주의'·'경고' 시간은 기록된 줄 수를 구간 길이로 환산한 <b>근사치</b>입니다 — 값이 올라오지 않은
        동안은 셀 수가 없습니다.
      </p>
    </div>
  );
}

// ── 최근 수신 이력 ────────────────────────────────────────────────────────

function RecentTable({ rows, worst }: { rows: SensorReading[]; worst: SensorStatusCode }) {
  if (rows.length === 0) return <div className="th-empty sm">아직 받은 값이 없습니다.</div>;
  return (
    <div className={`th-table-wrap ${worst === 'alert' ? 'alerting' : ''}`}>
      <table className="th-table">
        <thead>
          <tr><th>시간</th><th>센서</th><th>온도</th><th>습도</th><th>배터리</th><th>LQI</th><th>상태</th></tr>
        </thead>
        <tbody>
          {rows.map(r => (
            <tr key={`${r.deviceId}|${r.receivedAt}`}>
              <td>{clock(r.receivedAt)}</td>
              <td className="th-td-name">{r.deviceName}</td>
              <td>{fmt(r.temperature, '℃')}</td>
              <td>{fmt(r.humidity, '%')}</td>
              <td>{r.battery === null ? '--' : `${r.battery}%`}</td>
              <td>{r.linkQuality ?? '--'}</td>
              <td><span className={`th-badge sm st-${r.status}`}>{r.statusLabel}</span></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
