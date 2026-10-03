import { useEffect, useMemo, useState } from 'react';
import { api } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import { MOBILE_TAB_CHOICES } from '../components/Layout';
import './Security.css';

// 외부 접속 보안(관리자) — 사외(모바일 데이터 등)에서의 접속을 정한다. 접두사 sec-
//  · 설정: 제한 켜기/끄기, 사내로 볼 IP 대역, 사외에서 막을 메뉴(관리자 영역은 늘 막힌다)
//  · 계정: 사외 접속 허용 계정, 강제 로그아웃
//  · 접속 기록: 로그인 성공·실패·차단(사내/사외, IP, 기기)
// 제한을 켜기 전에도 기록은 남는다 — 먼저 며칠 기록을 보고 허용 계정을 정한 뒤 켜면 된다.

interface Config {
  enforce: boolean; internalRanges: string[]; externalHidden: string[];
  myIp: string; myInternal: boolean; disabledByConfig: boolean; serverGated: string[];
}
interface SecUser {
  id: number; username: string; realName: string; department: string; teamName: string; jobTitle: string;
  isAdmin: boolean; allowExternal: boolean; lastLogin: string | null; lastExternal: string | null; revokedAt: string | null;
}
interface Log { id: number; at: string; username: string; realName: string; ip: string; external: boolean; result: string; device: string }

const RESULT: Record<string, [string, string]> = {
  ok: ['성공', 'ok'], fail: ['비밀번호 틀림', 'fail'], blocked: ['사외 차단', 'blocked'], throttled: ['시도 과다', 'fail'],
};
const CHOICES = MOBILE_TAB_CHOICES.filter(c => !c.adminOnly);
const GROUPS = [...new Set(CHOICES.map(c => c.group))];

type Tab = 'config' | 'users' | 'logs';

export default function Security() {
  const [tab, setTab] = useState<Tab>('config');
  return (
    <div className="sec-page">
      <header className="pg-header">
        <div>
          <h2>외부 접속 보안</h2>
          <p>모바일 데이터 등 회사 밖에서의 접속을 정합니다 · 회사 와이파이·유선은 사내, 그 밖은 모두 사외</p>
        </div>
      </header>
      <div className="pg-body">
        <div className="sec-tabs" role="tablist">
          {([['config', '설정'], ['users', '계정 · 강제 로그아웃'], ['logs', '접속 기록']] as [Tab, string][]).map(([k, l]) => (
            <button key={k} type="button" role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{l}</button>
          ))}
        </div>
        {tab === 'config' && <ConfigTab />}
        {tab === 'users' && <UsersTab />}
        {tab === 'logs' && <LogsTab />}
      </div>
    </div>
  );
}

function ConfigTab() {
  const [cfg, setCfg] = useState<Config | null>(null);
  const [ranges, setRanges] = useState('');
  const [hidden, setHidden] = useState<Set<string>>(new Set());
  const [enforce, setEnforce] = useState(false);
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const apply = (c: Config) => {
    setCfg(c); setEnforce(c.enforce); setRanges(c.internalRanges.join('\n')); setHidden(new Set(c.externalHidden));
  };
  useEffect(() => { void api.get<Config>('/api/security/config').then(apply).catch(e => setMsg({ ok: false, text: e.message })); }, []);

  if (!cfg) return msg ? <div className="sec-msg bad">{msg.text}</div> : <div className="page-loading">불러오는 중…</div>;

  const gated = new Set(cfg.serverGated);
  const toggle = (to: string) => setHidden(s => { const n = new Set(s); if (n.has(to)) n.delete(to); else n.add(to); return n; });

  async function save() {
    if (enforce && !cfg!.enforce && !confirm(
      '외부 접속 제한을 켭니다.\n\n허용되지 않은 계정은 사외(모바일 데이터 등)에서 바로 끊기고 로그인할 수 없습니다.\n'
      + '먼저 [계정] 탭에서 밖에서 써야 하는 사람(생산팀 등)을 허용했는지 확인하세요.\n\n켤까요?')) return;
    setBusy(true); setMsg(null);
    try {
      const next = await api.put<Config>('/api/security/config', {
        enforce, internalRanges: ranges.split(/[\n,]+/).map(s => s.trim()).filter(Boolean), externalHidden: [...hidden],
      });
      apply(next);
      setMsg({ ok: true, text: '저장했습니다. 30초 안에 모든 접속에 적용됩니다.' });
    } catch (e) {
      setMsg({ ok: false, text: e instanceof Error ? e.message : '저장하지 못했습니다.' });
    } finally { setBusy(false); }
  }

  return (
    <div className="sec-grid">
      <section className="sec-card">
        <h3>외부 접속 제한</h3>
        <label className={`sec-switch${enforce ? ' on' : ''}`}>
          <input type="checkbox" checked={enforce} onChange={e => setEnforce(e.target.checked)} />
          <span className="knob" aria-hidden />
          <b>{enforce ? '켜짐 — 허용된 계정만 사외 접속' : '꺼짐 — 누구나 어디서든(기록만 남김)'}</b>
        </label>
        {cfg.disabledByConfig && (
          <div className="sec-msg bad">서버 설정 파일(appsettings.local.json)의 비상 해제(Security:DisableExternalLimit)가 켜져 있어 지금은 제한이 적용되지 않습니다.</div>
        )}
        <ul className="sec-notes">
          <li>켜면: 허용되지 않은 계정은 사외에서 로그인할 수 없고, 이미 로그인한 사람도 사외에서는 다음 요청부터 끊깁니다.</li>
          <li>허용된 계정도 사외에서는 <b>관리자 메뉴</b>와 아래에서 고른 메뉴를 열 수 없습니다.</li>
          <li>잠김 방지: 지금 이 PC 가 사내로 잡히지 않으면 켜지지 않습니다.</li>
        </ul>

        <h3 className="sec-h3-gap">사내로 볼 IP 대역</h3>
        <div className={`sec-myip ${cfg.myInternal ? 'in' : 'out'}`}>
          지금 이 PC: <b>{cfg.myIp || '알 수 없음'}</b> → <b>{cfg.myInternal ? '사내' : '사외'}</b>
          <span>(저장된 대역 기준)</span>
        </div>
        <textarea className="input sec-ranges" rows={6} value={ranges} onChange={e => setRanges(e.target.value)}
          placeholder={'한 줄에 하나\n10.0.0.0/8\n192.168.0.0/16'} spellCheck={false} />
        <p className="sec-hint">
          한 줄에 하나 — <code>10.10.0.0/16</code> 같은 대역 또는 <code>10.10.10.13</code> 같은 IP 하나.
          처음 값(10.x · 172.16~31.x · 192.168.x)은 사내 사설 IP 전부입니다. 서버 PC 자신은 늘 사내입니다.
          모바일 데이터는 통신사 공인 IP 로 들어오므로 여기에 넣지 않습니다.
        </p>

        <div className="sec-actions">
          {msg && <span className={`sec-msg ${msg.ok ? 'good' : 'bad'}`}>{msg.text}</span>}
          <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void save()}>{busy ? '저장 중…' : '저장'}</button>
        </div>
      </section>

      <section className="sec-card">
        <h3>사외에서 막을 메뉴 <em>{hidden.size}개</em></h3>
        <p className="sec-hint">
          허용된 계정이라도 사외에서는 열지 못합니다(관리자 포함). <span className="sec-tag srv">서버 차단</span> 은 서버도 막는 메뉴,
          <span className="sec-tag">화면만</span> 은 메뉴에서만 가립니다(다른 화면과 자료를 같이 써서 서버에서 막지 않음).
        </p>
        <div className="sec-menus">
          {GROUPS.map(g => (
            <div key={g} className="sec-mgroup">
              <div className="sec-mg-title">{g}</div>
              {CHOICES.filter(c => c.group === g).map(c => (
                <label key={c.to} className={`sec-mitem${hidden.has(c.to) ? ' on' : ''}`}>
                  <input type="checkbox" checked={hidden.has(c.to)} onChange={() => toggle(c.to)} />
                  <span>{c.label}</span>
                  <span className={`sec-tag${gated.has(c.to) ? ' srv' : ''}`}>{gated.has(c.to) ? '서버 차단' : '화면만'}</span>
                </label>
              ))}
            </div>
          ))}
        </div>
        <div className="sec-actions">
          <span className="sec-hint">메뉴 선택도 왼쪽 [저장]으로 함께 저장됩니다.</span>
          <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void save()}>{busy ? '저장 중…' : '저장'}</button>
        </div>
      </section>
    </div>
  );
}

function UsersTab() {
  const { user: me } = useAuth();
  const [users, setUsers] = useState<SecUser[] | null>(null);
  const [q, setQ] = useState('');
  const [dept, setDept] = useState('');
  const [onlyAllowed, setOnlyAllowed] = useState(false);
  const [sel, setSel] = useState<Set<number>>(new Set());
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);

  const load = () => api.get<SecUser[]>('/api/security/users').then(setUsers).catch(e => setMsg({ ok: false, text: e.message }));
  useEffect(() => { void load(); }, []);

  const depts = useMemo(() => [...new Set((users ?? []).map(u => u.department).filter(Boolean))].sort((a, b) => a.localeCompare(b, 'ko')), [users]);
  const shown = useMemo(() => (users ?? []).filter(u =>
    (!dept || u.department === dept)
    && (!onlyAllowed || u.allowExternal)
    && (!q.trim() || `${u.realName} ${u.username} ${u.teamName} ${u.jobTitle}`.toLowerCase().includes(q.trim().toLowerCase()))), [users, dept, onlyAllowed, q]);

  if (!users) return msg ? <div className="sec-msg bad">{msg.text}</div> : <div className="page-loading">불러오는 중…</div>;

  const allowedCount = users.filter(u => u.allowExternal).length;
  const allShownSelected = shown.length > 0 && shown.every(u => sel.has(u.id));

  async function setAllow(ids: number[], allow: boolean) {
    if (ids.length === 0) return;
    setBusy(true); setMsg(null);
    try {
      await api.post('/api/security/users/allow', { ids, allow });
      setUsers(us => us!.map(u => (ids.includes(u.id) ? { ...u, allowExternal: allow } : u)));
      setMsg({ ok: true, text: `${ids.length}명 사외 접속 ${allow ? '허용' : '해제'}` });
    } catch (e) { setMsg({ ok: false, text: e instanceof Error ? e.message : '바꾸지 못했습니다.' }); }
    finally { setBusy(false); }
  }

  async function revoke(u: SecUser) {
    const self = u.id === me?.id;
    if (!confirm(`${u.realName}(${u.username}) 의 로그인을 모든 기기에서 끊을까요?\n\n`
      + (self ? '본인 계정입니다 — 이 화면도 바로 로그인 창이 뜹니다.\n' : '')
      + '다시 로그인하면 들어올 수 있습니다. 휴대폰 분실이면 비밀번호도 바꾸거나 사외 허용을 해제하세요.')) return;
    setBusy(true); setMsg(null);
    try {
      const at = await api.post<string>(`/api/security/users/${u.id}/revoke`, {});
      setUsers(us => us!.map(x => (x.id === u.id ? { ...x, revokedAt: at } : x)));
      setMsg({ ok: true, text: `${u.realName} 로그아웃시켰습니다.` });
    } catch (e) { setMsg({ ok: false, text: e instanceof Error ? e.message : '로그아웃시키지 못했습니다.' }); }
    finally { setBusy(false); }
  }

  const toggleSel = (id: number) => setSel(s => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });

  return (
    <section className="sec-card">
      <div className="sec-toolbar">
        <input className="input" placeholder="이름·아이디·팀 검색" value={q} onChange={e => setQ(e.target.value)} />
        <select className="input" value={dept} onChange={e => setDept(e.target.value)}>
          <option value="">부서 전체</option>
          {depts.map(d => <option key={d} value={d}>{d}</option>)}
        </select>
        <label className="sec-chk"><input type="checkbox" checked={onlyAllowed} onChange={e => setOnlyAllowed(e.target.checked)} /> 허용된 계정만</label>
        <span className="sec-count">사외 허용 <b>{allowedCount}</b> / {users.length}명</span>
      </div>
      <div className="sec-bulk">
        <span>{sel.size > 0 ? `${sel.size}명 선택` : '체크해서 여러 명을 한 번에'}</span>
        <button type="button" className="btn btn-ghost" disabled={busy || sel.size === 0} onClick={() => void setAllow([...sel], true)}>선택 허용</button>
        <button type="button" className="btn btn-ghost" disabled={busy || sel.size === 0} onClick={() => void setAllow([...sel], false)}>선택 해제</button>
        {sel.size > 0 && <button type="button" className="btn btn-ghost" onClick={() => setSel(new Set())}>선택 지우기</button>}
        {msg && <span className={`sec-msg ${msg.ok ? 'good' : 'bad'}`}>{msg.text}</span>}
      </div>
      <div className="sec-table-wrap">
        <table className="sec-table">
          <thead>
            <tr>
              <th className="c-chk">
                <input type="checkbox" aria-label="보이는 사람 모두 선택" checked={allShownSelected}
                  onChange={() => setSel(s => {
                    const n = new Set(s);
                    if (allShownSelected) shown.forEach(u => n.delete(u.id)); else shown.forEach(u => n.add(u.id));
                    return n;
                  })} />
              </th>
              <th>이름</th><th>부서 · 팀</th><th className="c-allow">사외 접속</th>
              <th>마지막 로그인</th><th>마지막 사외 로그인</th><th className="c-act">강제 로그아웃</th>
            </tr>
          </thead>
          <tbody>
            {shown.map(u => (
              <tr key={u.id} className={u.allowExternal ? 'allowed' : ''}>
                <td className="c-chk"><input type="checkbox" checked={sel.has(u.id)} onChange={() => toggleSel(u.id)} aria-label={`${u.realName} 선택`} /></td>
                <td>
                  <b>{u.realName}</b> <span className="sec-sub">{u.username}</span>
                  {u.isAdmin && <span className="sec-tag adm">관리자</span>}
                  {u.jobTitle && <div className="sec-sub">{u.jobTitle}</div>}
                </td>
                <td>{u.department}{u.teamName ? <span className="sec-sub"> · {u.teamName}</span> : null}</td>
                <td className="c-allow">
                  <button type="button" className={`sec-pill${u.allowExternal ? ' on' : ''}`} disabled={busy}
                    onClick={() => void setAllow([u.id], !u.allowExternal)} aria-pressed={u.allowExternal}>
                    {u.allowExternal ? '허용' : '사내만'}
                  </button>
                </td>
                <td className="sec-time">{u.lastLogin ?? '—'}</td>
                <td className="sec-time">{u.lastExternal ?? '—'}</td>
                <td className="c-act">
                  <button type="button" className="btn btn-ghost sec-revoke" disabled={busy} onClick={() => void revoke(u)}>로그아웃</button>
                  {u.revokedAt && <div className="sec-sub">{u.revokedAt.slice(5, 16)} 실행</div>}
                </td>
              </tr>
            ))}
            {shown.length === 0 && <tr><td colSpan={7} className="sec-empty">해당하는 계정이 없습니다.</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function LogsTab() {
  const [rows, setRows] = useState<Log[] | null>(null);
  const [days, setDays] = useState(7);
  const [extOnly, setExtOnly] = useState(false);
  const [q, setQ] = useState('');
  const [err, setErr] = useState('');

  useEffect(() => {
    let alive = true;
    const t = setTimeout(() => {
      const p = new URLSearchParams({ days: String(days), externalOnly: String(extOnly) });
      if (q.trim()) p.set('q', q.trim());
      api.get<Log[]>(`/api/security/logs?${p}`)
        .then(r => { if (alive) { setRows(r); setErr(''); } })
        .catch(e => { if (alive) setErr(e.message); });
    }, q ? 300 : 0);
    return () => { alive = false; clearTimeout(t); };
  }, [days, extOnly, q]);

  const fails = (rows ?? []).filter(r => r.result !== 'ok').length;

  return (
    <section className="sec-card">
      <div className="sec-toolbar">
        <select className="input" value={days} onChange={e => setDays(Number(e.target.value))}>
          {[1, 7, 30, 90, 180].map(d => <option key={d} value={d}>{d === 1 ? '오늘' : `최근 ${d}일`}</option>)}
        </select>
        <input className="input" placeholder="이름·아이디·IP 검색" value={q} onChange={e => setQ(e.target.value)} />
        <label className="sec-chk"><input type="checkbox" checked={extOnly} onChange={e => setExtOnly(e.target.checked)} /> 사외만</label>
        {rows && <span className="sec-count">{rows.length}건{rows.length >= 500 ? '(최근 500건까지)' : ''} · 실패·차단 <b className={fails ? 'bad' : ''}>{fails}</b></span>}
      </div>
      {err && <div className="sec-msg bad">{err}</div>}
      <p className="sec-hint">로그인할 때마다 한 줄 남습니다(180일 보관). 모르는 IP 에서 실패가 이어지면 그 계정의 비밀번호를 바꾸세요.</p>
      <div className="sec-table-wrap">
        <table className="sec-table">
          <thead><tr><th>시각</th><th>계정</th><th>결과</th><th>위치</th><th>IP</th><th>기기</th></tr></thead>
          <tbody>
            {(rows ?? []).map(r => {
              const [label, cls] = RESULT[r.result] ?? [r.result, ''];
              return (
                <tr key={r.id}>
                  <td className="sec-time">{r.at}</td>
                  <td>{r.realName ? <><b>{r.realName}</b> <span className="sec-sub">{r.username}</span></> : <span className="sec-sub">{r.username || '—'}</span>}</td>
                  <td><span className={`sec-res ${cls}`}>{label}</span></td>
                  <td><span className={`sec-zone ${r.external ? 'out' : 'in'}`}>{r.external ? '사외' : '사내'}</span></td>
                  <td className="sec-ip">{r.ip}</td>
                  <td>{r.device}</td>
                </tr>
              );
            })}
            {rows && rows.length === 0 && <tr><td colSpan={6} className="sec-empty">기록이 없습니다.</td></tr>}
          </tbody>
        </table>
      </div>
    </section>
  );
}
