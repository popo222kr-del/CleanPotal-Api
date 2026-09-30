import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api, ApiError } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import type { CheckPhoto, CheckResult, CheckSheet, CheckSheetItem } from '../../api/types';
import AttImage from '../../components/AttImage';
import PhotoPopup from './PhotoPopup';
import QrScanButton, { QrIcon } from '../../components/QrScan';
import { filesToAtts } from '../attach';
import { dayLabel, PHOTO_LABEL, photoRequired, photoSlots, timeLabel } from './common';
import './Checklist.css';

// 구역 QR 을 찍으면 열리는 화면 — 휴대폰에서 쓴다.
// 항목마다 누르는 즉시 저장한다(전파가 끊기거나 창을 닫아도 한 데까지는 남는다). "제출" 은 마감이다.

type Draft = { result: string; numValue: number | null; memo: string; photos: CheckPhoto[] };

const GROUP_TITLE: Record<string, string> = { common: '공통 항목', weekly: '주 1회 항목', event: '필요할 때만' };

function draftOf(r: CheckResult | null): Draft {
  return r
    ? { result: r.result, numValue: r.numValue, memo: r.memo, photos: r.photos }
    : { result: '', numValue: null, memo: '', photos: [] };
}

export default function CheckZone() {
  const { code = '' } = useParams();
  const [params] = useSearchParams();
  const [sheet, setSheet] = useState<CheckSheet | null>(null);
  const [error, setError] = useState('');
  const { user } = useAuth();
  const [busy, setBusy] = useState<Record<number, boolean>>({});
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<string | null>(null);
  const reasonRef = useRef('');
  // 현황 화면에서 누르고 들어온 것은 QR 로 들어온 것이 아니다.
  const viaQr = params.get('from') !== 'hub';
  // 매일 하는 항목과 주 1회 항목을 한 줄로 늘어놓으면 무엇을 오늘 해야 하는지 헷갈렸다 — 탭으로 나눈다.
  const [tab, setTab] = useState<'daily' | 'weekly'>(params.get('tab') === 'weekly' ? 'weekly' : 'daily');

  const load = useCallback(async () => {
    const q = new URLSearchParams();
    if (params.get('date')) q.set('date', params.get('date')!);
    if (params.get('shift')) q.set('shift', params.get('shift')!);
    try {
      setSheet(await api.get<CheckSheet>(`/api/checklist/sheet/${encodeURIComponent(code)}${q.toString() ? `?${q}` : ''}`));
      setError('');
    } catch (e) {
      // 권한이 없으면 '요청 실패 (403)' 만 떠서 QR 이 고장 난 것처럼 보였다 — 무엇을 받아야 하는지 알려 준다.
      // 다른 부서 구역이면 서버가 그 이유를 보낸다 — 권한 안내 대신 그 문구를 그대로 보여 준다.
      if (e instanceof ApiError && e.status === 403) setError(e.message.includes('다른 부서') ? e.message : '403');
      // 로그인 만료(401)는 오류 화면을 띄우지 않는다 — 다시 로그인 창에서 로그인하면 아래 효과가 다시 불러온다.
      else if (e instanceof ApiError && e.status === 401) setError('');
      else setError(e instanceof Error ? e.message : '점검표를 불러오지 못했습니다.');
    }
  }, [code, params]);
  // user 가 바뀌면(다시 로그인 창에서 로그인 등) 점검표를 다시 불러온다.
  useEffect(() => { load(); }, [load, user]);

  async function save(item: CheckSheetItem, next: Draft) {
    if (!sheet) return;
    let reason: string | null = null;
    if (sheet.needsReason) {
      reason = reasonRef.current || prompt('제출된 점검을 고칩니다. 수정 사유를 적어 주세요.')?.trim() || '';
      if (!reason) return;
      reasonRef.current = reason;
    }
    // 화면에 먼저 반영하고 서버 응답으로 맞춘다.
    const optimistic: CheckResult = {
      id: item.result?.id ?? 0, result: next.result as CheckResult['result'], numValue: next.numValue, memo: next.memo,
      photos: next.photos, checkedAt: new Date().toISOString(), checkedByName: item.result?.checkedByName ?? '',
      ngStatus: item.result?.ngStatus ?? '',
    };
    setSheet(s => s && { ...s, items: s.items.map(i => i.itemId === item.itemId ? { ...i, result: optimistic } : i) });
    setBusy(b => ({ ...b, [item.itemId]: true }));
    try {
      const saved = await api.put<CheckResult | null>(`/api/checklist/sheet/${encodeURIComponent(sheet.zoneCode)}/items/${item.itemId}`, {
        date: sheet.workDate, shift: sheet.shift, result: next.result, numValue: next.numValue,
        memo: next.memo, photos: next.photos, viaQr, reason,
      });
      setSheet(s => s && { ...s, items: s.items.map(i => i.itemId === item.itemId ? { ...i, result: saved } : i) });
      // 주 1회 항목은 저장하면 상태(작업 중·필수 여부)가 바뀐다 — 서버 판정으로 다시 받는다.
      if (item.group === 'weekly') load();
    } catch (e) {
      alert(e instanceof Error ? e.message : '저장하지 못했습니다.');
      reasonRef.current = '';
      load();
    } finally {
      setBusy(b => ({ ...b, [item.itemId]: false }));
    }
  }

  async function submit() {
    if (!sheet) return;
    const missingList = sheet.items.filter(i => i.required && !i.result?.result);
    const missingWeekly = missingList.filter(i => i.group === 'weekly').length;
    const missingDaily = missingList.length - missingWeekly;
    if (missingList.length > 0 && !confirm(
      `아직 입력하지 않은 필수 항목이 있습니다 — 매일 점검 ${missingDaily}개, 주 1회 ${missingWeekly}개.\n그래도 제출해 볼까요?`)) return;
    setSubmitting(true);
    try {
      setSheet(await api.post<CheckSheet>(`/api/checklist/sheet/${encodeURIComponent(sheet.zoneCode)}/submit`, {
        date: sheet.workDate, shift: sheet.shift, viaQr,
      }));
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } catch (e) {
      alert(e instanceof Error ? e.message : '제출하지 못했습니다.');
    } finally {
      setSubmitting(false);
    }
  }

  if (error === '403') {
    return (
      <div className="ck-zone">
        <div className="ck-error">
          <b>{user?.realName ?? '이 계정'}</b> 계정에는 체크시트 권한이 없습니다.<br />
          관리자에게 <b>사용자 계정 관리 → 권한</b>에서 <b>'설비·공정 관리'를 '조회' 이상</b>으로 받은 뒤 QR 을 다시 찍어 주세요.
        </div>
      </div>
    );
  }
  if (error) {
    return (
      <div className="ck-zone">
        <div className="ck-error">{error}</div>
        <Link className="btn btn-ghost" to="/checklist">체크시트 현황으로</Link>
      </div>
    );
  }
  if (!sheet) return <div className="ck-zone"><div className="ck-empty">불러오는 중…</div></div>;

  const required = sheet.items.filter(i => i.required);
  const doneCount = required.filter(i => i.result?.result).length;
  const dailyGroups = ['common', 'zone', 'event'] as const;
  const readOnly = !sheet.canEdit;

  // 주 1회: 해야 할 것(작업 중 → 밀림 → 오늘 → 이번 주) / 다가오는 요일(예정) / 이번 주 다른 교대에서 끝낸 것
  // 작업 중 = 작업 전 사진만 올려 두고 조치가 끝나기를 기다리는 항목(다른 교대에서 시작한 것도 이어 온다).
  const weekly = sheet.items.filter(i => i.group === 'weekly');
  const dueRank: Record<string, number> = { '작업 중': -1, 밀림: 0, 오늘: 1, '이번 주': 2 };
  const weeklyTodo = weekly.filter(i => !i.doneElsewhere && i.dueState in dueRank)
    .sort((a, b) => dueRank[a.dueState] - dueRank[b.dueState]);
  const weeklyUpcoming = weekly.filter(i => !i.doneElsewhere && i.dueState === '예정');
  const weeklyDone = weekly.filter(i => !!i.doneElsewhere);
  const weeklyLeft = weeklyTodo.filter(i => !i.result?.result);
  const weeklyOverdue = weeklyLeft.some(i => i.dueState === '밀림');
  const dailyReq = required.filter(i => i.group !== 'weekly');
  const dailyDone = dailyReq.filter(i => i.result?.result).length;

  const card = (item: CheckSheetItem) => (
    <ItemCard key={item.itemId} item={item} readOnly={readOnly} busy={!!busy[item.itemId]}
      photoLabel={`${sheet.shift}_${sheet.zoneCode} ${sheet.zoneName}_${item.code}`}
      onSave={d => save(item, d)} onPreview={setPreview} />
  );

  return (
    <div className="ck-zone">
      <header className="ck-zhead">
        <div className="ck-zline">
          <span>{sheet.line} · {sheet.zoneCode}</span>
          {/* 다음 구역으로 옮길 때 — 웹앱 안에서 바로 찍는다 */}
          <QrScanButton className="ck-zscan"><span className="ck-scan-ico">{QrIcon}</span>다른 구역 QR</QrScanButton>
        </div>
        <h2>{sheet.zoneName}</h2>
        <div className="ck-zmeta">
          <span>{dayLabel(sheet.workDate)}</span>
          <span className={`ck-shift ${sheet.shift === '주간' ? 'day' : 'night'}`}>{sheet.shift}</span>
          {!sheet.isCurrent && <span className="ck-tag warn">지금 교대 아님</span>}
        </div>
      </header>

      {sheet.submittedAt && (
        <div className="ck-banner ok">
          제출 완료 — {timeLabel(sheet.submittedAt)} {sheet.submittedByName}
          {sheet.needsReason && <div className="ck-banner-sub">관리자 수정 모드: 고치면 사유와 이전 값이 이력에 남습니다.</div>}
        </div>
      )}
      {!sheet.submittedAt && readOnly && (
        <div className="ck-banner">
          {sheet.isFuture ? '아직 시작하지 않은 교대라 입력할 수 없습니다.'
            : sheet.isCurrent ? '점검할 권한이 없습니다(사용자 관리에서 설비·공정 관리 조회 등급 이상 필요).'
            : '지금 교대나 바로 앞 교대가 아니라서 입력할 수 없습니다.'}
        </div>
      )}

      {weekly.length > 0 && (
        <div className="ck-tabs" role="tablist">
          <button role="tab" aria-selected={tab === 'daily'} className={tab === 'daily' ? 'on' : ''} onClick={() => setTab('daily')}>
            매일 점검 <span className="ck-tabn">{dailyDone}/{dailyReq.length}</span>
          </button>
          <button role="tab" aria-selected={tab === 'weekly'} className={`wk ${tab === 'weekly' ? 'on' : ''}`} onClick={() => setTab('weekly')}>
            주 1회
            {weeklyLeft.length > 0
              ? <span className={`ck-tabn ${weeklyOverdue ? 'bad' : 'warn'}`}>{weeklyOverdue ? '밀림 ' : '할 일 '}{weeklyLeft.length}</span>
              : <span className="ck-tabn ok">✓</span>}
          </button>
        </div>
      )}

      {(tab === 'daily' || weekly.length === 0) && dailyGroups.map(g => {
        const list = sheet.items.filter(i => i.group === g);
        if (list.length === 0) return null;
        return (
          <section key={g} className="ck-group">
            <h3>{g === 'zone' ? `${sheet.zoneName} 항목` : GROUP_TITLE[g]}</h3>
            {list.map(card)}
          </section>
        );
      })}

      {tab === 'weekly' && weekly.length > 0 && (
        <div className="ck-weekly">
          <p className="ck-wk-hint">이번 주(월~일) 안에 <b>한 번만</b> 하면 되는 항목입니다. 정해진 요일이 되면 "오늘", 지나면 "밀림"으로 바뀝니다.<br />
            조치가 바로 안 끝나면 <b>작업 전 사진만 먼저</b> 올려 두세요 — "작업 중"으로 남고 매일 점검 제출은 막지 않습니다.
            다음 교대·다음 날 QR 로 들어와 <b>작업 후 사진</b>을 찍고 OK 를 누르면 끝납니다.</p>
          <section className="ck-group">
            <h3>이번 주에 할 항목 {weeklyTodo.length > 0 && <em>{weeklyTodo.length}</em>}</h3>
            {weeklyTodo.length === 0 ? <div className="ck-wk-empty">지금 할 주 1회 항목이 없습니다.</div> : weeklyTodo.map(card)}
          </section>
          {weeklyUpcoming.length > 0 && (
            <section className="ck-group">
              <h3>다가오는 요일 <em>{weeklyUpcoming.length}</em></h3>
              <div className="ck-wk-sub">그 요일에 하면 됩니다. 미리 해도 이번 주 완료로 칩니다.</div>
              {weeklyUpcoming.map(card)}
            </section>
          )}
          {weeklyDone.length > 0 && (
            <section className="ck-group">
              <h3>이번 주 완료 <em>{weeklyDone.length}</em></h3>
              {weeklyDone.map(card)}
            </section>
          )}
        </div>
      )}
      {sheet.items.length === 0 && <div className="ck-empty">이 교대에 점검할 항목이 없습니다.</div>}

      <div className="ck-footer">
        <div className="ck-progress">
          <b>{doneCount}</b> / {required.length}
          <div className="ck-bar"><i style={{ width: `${required.length ? (doneCount / required.length) * 100 : 100}%` }} /></div>
        </div>
        {!sheet.submittedAt && !readOnly && (
          <button className={`ck-submit ${doneCount === required.length ? 'ready' : ''}`} disabled={submitting} onClick={submit}>
            {submitting ? '제출 중…' : '제출'}
          </button>
        )}
        <Link className="ck-hub" to="/checklist">현황</Link>
      </div>

      {preview && <PhotoPopup value={preview} onClose={() => setPreview(null)} />}
    </div>
  );
}

function ItemCard({ item, readOnly, busy, photoLabel, onSave, onPreview }: {
  item: CheckSheetItem; readOnly: boolean; busy: boolean;
  /** 보관소 파일 이름에 붙는 설명 — "교대_구역_항목" 뒤에 사진 종류가 붙는다. */
  photoLabel: string;
  onSave: (d: Draft) => void; onPreview: (v: string) => void;
}) {
  const d = draftOf(item.result);
  const [num, setNum] = useState(d.numValue?.toString() ?? '');
  const [memo, setMemo] = useState(d.memo);
  useEffect(() => { setNum(item.result?.numValue?.toString() ?? ''); setMemo(item.result?.memo ?? ''); }, [item.result?.numValue, item.result?.memo]);

  // 사진을 올리는 동안(4G 에서 몇 초)은 다른 버튼을 막는다. 예전에는 올리기 전에 잡아 둔 결과로 저장해
  // 그 사이 누른 NG 가 되돌아가거나, 두 장을 연달아 올리면 앞 사진이 빠졌다.
  const [uploading, setUploading] = useState(false);
  const itemRef = useRef(item);
  itemRef.current = item;
  const memoRef = useRef(memo);
  memoRef.current = memo;

  const done = !!item.doneElsewhere;
  const disabled = readOnly || done || busy || uploading;
  const result = d.result;

  function pick(r: string) {
    if (disabled) return;
    onSave({ ...d, result: result === r ? '' : r, memo });
  }
  function commitNum() {
    if (disabled) return;
    const t = num.trim();
    const v = t === '' ? null : Number(t);
    if (v !== null && !Number.isFinite(v)) { alert('숫자를 넣어 주세요.'); return; }
    if (v === d.numValue && result !== 'NA') return;
    onSave({ ...d, result: v === null ? '' : 'OK', numValue: v, memo });
  }
  function commitMemo() {
    if (disabled || memo === d.memo) return;
    onSave({ ...d, memo });
  }
  async function addPhoto(slot: CheckPhoto['k'], file: File | undefined) {
    if (!file || disabled) return;
    setUploading(true);
    try {
      const refs = await filesToAtts([file], { imagesOnly: true, scope: 'field', cat: '체크시트', label: `${photoLabel}_${PHOTO_LABEL[slot]}` });
      if (refs.length === 0) return;
      // 올리는 동안 바뀌었을 수 있으니 지금 결과를 다시 읽어 그 위에 사진만 더한다.
      const now = draftOf(itemRef.current.result);
      onSave({ ...now, memo: memoRef.current, photos: [...now.photos.filter(p => p.k !== slot), { k: slot, v: refs[0] }] });
    } finally {
      setUploading(false);
    }
  }
  function removePhoto(slot: string) {
    if (disabled || !confirm(`${PHOTO_LABEL[slot]} 사진을 지울까요?`)) return;
    onSave({ ...d, memo, photos: d.photos.filter(p => p.k !== slot) });
  }

  const slots = photoSlots(item.photoPolicy, result);
  const isNum = item.resultType === 'NUM';
  const state = result === 'OK' ? 'ok' : result === 'NG' ? 'ng' : result === 'NA' ? 'na' : '';

  return (
    <div className={`ck-item ${state} ${done ? 'done' : ''}`}>
      <div className="ck-ihead">
        <span className="ck-code">{item.code}</span>
        {item.group === 'weekly' && item.dueState && (
          <span className={`ck-tag ${item.dueState === '작업 중' ? 'work' : item.dueState === '밀림' ? 'bad' : item.dueState === '오늘' ? 'warn' : ''}`}>
            {item.weekdayLabel ? `${item.weekdayLabel}요일 · ` : ''}{item.dueState}
          </span>
        )}
        {!item.required && item.group !== 'weekly' && <span className="ck-tag">선택</span>}
        {(busy || uploading) && <span className="ck-saving">{uploading ? '사진 올리는 중…' : '저장 중…'}</span>}
      </div>
      <div className="ck-text">{item.text}</div>
      {item.detail && <div className="ck-detail">{item.detail}</div>}
      {item.specText && <div className="ck-spec">기준 {item.specText}</div>}
      {item.dueState === '작업 중' && !done && (
        <div className="ck-working">
          {item.workingFrom ? <>작업 전 사진: <b>{item.workingFrom}</b><br /></> : '작업 전 사진을 올렸습니다. '}
          조치가 끝나면 <b>작업 후 사진</b>을 찍고 OK 를 누르세요.
        </div>
      )}

      {done ? (
        <div className="ck-elsewhere">이번 주 완료 — {item.doneElsewhere}</div>
      ) : (
        <>
          {isNum ? (
            <div className="ck-num">
              <input className="input" inputMode="decimal" placeholder="측정값" value={num} disabled={disabled || result === 'NA'}
                onChange={e => setNum(e.target.value.replace(/[^0-9.-]/g, ''))}
                onBlur={commitNum} onKeyDown={e => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }} />
              <span className="ck-unit">{item.unit}</span>
              {(result === 'OK' || result === 'NG') && <span className={`ck-judge ${state}`}>{result === 'OK' ? '합격' : '불합격'}</span>}
              {item.allowNa && (
                <button className={`ck-btn na ${result === 'NA' ? 'on' : ''}`} disabled={disabled} onClick={() => pick('NA')}>N/A</button>
              )}
            </div>
          ) : (
            <div className="ck-btns">
              <button className={`ck-btn ok ${result === 'OK' ? 'on' : ''}`} disabled={disabled} onClick={() => pick('OK')}>OK</button>
              <button className={`ck-btn ng ${result === 'NG' ? 'on' : ''}`} disabled={disabled} onClick={() => pick('NG')}>NG</button>
              {item.allowNa && <button className={`ck-btn na ${result === 'NA' ? 'on' : ''}`} disabled={disabled} onClick={() => pick('NA')}>N/A</button>}
            </div>
          )}

          {slots.length > 0 && result !== 'NA' && (
            <div className="ck-photos">
              {slots.map(slot => {
                const p = d.photos.find(x => x.k === slot);
                const need = photoRequired(item.photoPolicy, slot, result || 'OK');
                return (
                  // 결과를 고르기 전에는 빨갛게 재촉하지 않는다 — 고른 뒤에 빠진 사진만 표시한다.
                  <div key={slot} className={`ck-photo ${p ? 'has' : need && result ? 'need' : ''}`}>
                    {p ? (
                      <>
                        <AttImage value={p.v} className="ck-thumb" onClick={() => onPreview(p.v)} />
                        <div className="ck-plabel">{PHOTO_LABEL[slot]}{!disabled && <button onClick={() => removePhoto(slot)}>지우기</button>}</div>
                      </>
                    ) : (
                      <label className={`ck-shot ${disabled ? 'off' : ''}`}>
                        <input type="file" accept="image/*" capture="environment" disabled={disabled}
                          onChange={e => { addPhoto(slot, e.target.files?.[0]); e.target.value = ''; }} />
                        <span className="ck-cam">📷</span>
                        <span>{PHOTO_LABEL[slot]} 사진{need ? '' : '(선택)'}</span>
                      </label>
                    )}
                  </div>
                );
              })}
            </div>
          )}

          {(result === 'NG' || memo || (!disabled && isNum)) && (
            <textarea className={`ck-memo ${result === 'NG' && !memo.trim() ? 'need' : ''}`} rows={2} disabled={disabled}
              placeholder={result === 'NG' ? 'NG 사유 (필수)' : '메모 (선택)'} value={memo}
              onChange={e => setMemo(e.target.value)} onBlur={commitMemo} />
          )}
          {item.result?.checkedByName && item.result.result && (
            <div className="ck-who">{item.result.checkedByName} · {timeLabel(item.result.checkedAt)}</div>
          )}
        </>
      )}
    </div>
  );
}
