import { useCallback, useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import { useAccess } from '../auth/useAccess';
import { useIsMobile } from '../hooks/useIsMobile';
import TempHumidityLimits from './TempHumidityLimits';
import type { SensorHistory, SensorReading, SensorSnapshot, SensorStatusCode, ZigbeeStatus } from '../api/types';
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
/** 추이 그래프가 가져오는 점 개수와 구간. */
const HISTORY_LIMIT = 1500;
const HISTORY_HOURS = 24;
/** 아래 표에 보여 줄 최근 수신 줄 수. */
const RECENT_LIMIT = 50;

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

  // 그래프용 이력. 센서 목록이 정해진 뒤 한 번, 그리고 갱신 주기의 6배마다 다시 읽는다.
  const deviceIds = useMemo(() => sensors.map(s => s.deviceId).join(','), [sensors]);
  const loadHistory = useCallback(async () => {
    const ids = deviceIds ? deviceIds.split(',') : [];
    if (ids.length === 0) return;
    const rows = await Promise.all(ids.map(id =>
      api.get<SensorHistory>(`/api/iot/zigbee/history/${encodeURIComponent(id)}?hours=${HISTORY_HOURS}&limit=${HISTORY_LIMIT}`)
        .catch(() => ({ deviceId: id, deviceName: id, points: [] } as SensorHistory))));
    setHistories(rows);
  }, [deviceIds]);

  useEffect(() => {
    void loadHistory();
    const t = setInterval(() => { void loadHistory(); }, REFRESH_MS * 6);
    return () => clearInterval(t);
  }, [loadHistory]);

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
        <div>
          <h2>동탄 물류창고 온·습도 모니터링</h2>
          <p>Zigbee 센서 기반 실시간 온·습도 모니터링</p>
        </div>
        {isAdmin && <button className="btn btn-ghost" onClick={() => setLimitsOpen(true)}>기준 설정</button>}
        <SystemStatus status={status} />
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
                <b>{HISTORY_HOURS}시간 추이</b>
                <div className="th-picks">
                  <button className={`th-pick ${pick === 'all' ? 'on' : ''}`} onClick={() => setPick('all')}>전체</button>
                  {sensors.map(s => (
                    <button key={s.deviceId} className={`th-pick ${pick === s.deviceId ? 'on' : ''}`}
                            onClick={() => setPick(s.deviceId)}>{s.deviceName}</button>
                  ))}
                </div>
              </div>
              <div className="th-charts">
                <TrendChart title="온도" unit="℃" histories={shownHistories} field="temperature" />
                <TrendChart title="습도" unit="%" histories={shownHistories} field="humidity" />
              </div>
            </section>

            <section className="th-sec">
              <div className="th-sec-head"><b>최근 수신 이력</b><span className="th-dim">{recent.length}건</span></div>
              <RecentTable rows={recent} worst={worst} />
            </section>
          </>
        )}
      </div>

      {limitsOpen && (
        <TempHumidityLimits
          onClose={() => setLimitsOpen(false)}
          onSaved={() => { void loadLatest(); }}
        />
      )}
    </div>
  );
}

// ── 수집 계통 표시 ────────────────────────────────────────────────────────

function SystemStatus({ status }: { status: ZigbeeStatus | null }) {
  if (!status) return null;
  const mqtt = status.mqttOnline;
  const z2m = status.zigbee2mqttOnline;
  return (
    <div className="th-sys" title={status.message ?? undefined}>
      <span className={`th-sys-row ${mqtt ? 'ok' : 'bad'}`}>
        <i /> MQTT Broker {mqtt ? '정상' : '연결 실패'}
      </span>
      <span className={`th-sys-row ${z2m === false ? 'bad' : z2m ? 'ok' : 'idle'}`}>
        <i /> Zigbee2MQTT {z2m === false ? '중지' : z2m ? '정상' : '확인 중'}
      </span>
      <span className={`th-sys-row ${status.sensorsOnline === status.sensorsTotal && status.sensorsTotal > 0 ? 'ok' : 'bad'}`}>
        <i /> 센서 {status.sensorsOnline} / {status.sensorsTotal} 연결
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

function TrendChart({ title, unit, histories, field }: {
  title: string; unit: string; histories: SensorHistory[]; field: 'temperature' | 'humidity';
}) {
  const since = Date.now() - HISTORY_HOURS * 3600_000;
  const series = histories.map((h, i) => ({
    name: h.deviceName,
    color: LINE_COLORS[i % LINE_COLORS.length],
    pts: h.points
      .map(p => ({ t: new Date(p.receivedAt).getTime(), v: p[field] }))
      .filter(p => !Number.isNaN(p.t) && p.t >= since && p.v !== null) as { t: number; v: number }[],
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

  const W = 640, H = 220, padT = 12, padB = 30, padL = 44, padR = 12;
  const plotW = W - padL - padR;
  const plotH = H - padT - padB;
  const x = (t: number) => padL + (tMax === tMin ? plotW / 2 : ((t - tMin) / (tMax - tMin)) * plotW);
  const y = (v: number) => padT + plotH * (1 - (v - lo) / (hi - lo));
  const ticks = [0, 0.25, 0.5, 0.75, 1];

  return (
    <div className="th-chart-box">
      <div className="th-chart-title">{title} 추이</div>
      <div className="th-chart-scroll">
        <svg viewBox={`0 0 ${W} ${H}`} className="th-chart" preserveAspectRatio="none">
          {ticks.map(t => (
            <g key={t}>
              <line x1={padL} x2={W - padR} y1={padT + plotH * t} y2={padT + plotH * t} stroke="#F1F5F9" />
              <text x={padL - 6} y={padT + plotH * t + 4} fontSize={10} fill="#9CA3AF" textAnchor="end">
                {(hi - (hi - lo) * t).toFixed(1)}
              </text>
            </g>
          ))}
          <text x={10} y={H / 2} fontSize={10} fill="#6B7280" transform={`rotate(-90 10 ${H / 2})`} textAnchor="middle">{unit}</text>
          {[0, 0.5, 1].map(t => {
            const at = tMin + (tMax - tMin) * t;
            return (
              <text key={t} x={x(at)} y={H - 10} fontSize={10} fill="#64748B"
                    textAnchor={t === 0 ? 'start' : t === 1 ? 'end' : 'middle'}>
                {new Date(at).toLocaleTimeString('ko-KR', { hour12: false, hour: '2-digit', minute: '2-digit' })}
              </text>
            );
          })}
          {series.map(s => (
            <polyline key={s.name} fill="none" stroke={s.color} strokeWidth={1.8} strokeLinejoin="round"
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
