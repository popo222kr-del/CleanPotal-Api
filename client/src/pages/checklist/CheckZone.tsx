import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../../api/client';
import type { CheckPhoto, CheckResult, CheckSheet, CheckSheetItem } from '../../api/types';
import AttImage from '../../components/AttImage';
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
  const [busy, setBusy] = useState<Record<number, boolean>>({});
  const [submitting, setSubmitting] = useState(false);
  const [preview, setPreview] = useState<string | null>(null);
  const reasonRef = useRef('');
  // 현황 화면에서 누르고 들어온 것은 QR 로 들어온 것이 아니다.
  const viaQr = params.get('from') !== 'hub';

  const load = useCallback(async () => {
    const q = new URLSearchParams();
    if (params.get('date')) q.set('date', params.get('date')!);
    if (params.get('shift')) q.set('shift', params.get('shift')!);
    try {
      setSheet(await api.get<CheckSheet>(`/api/checklist/sheet/${encodeURIComponent(code)}${q.toString() ? `?${q}` : ''}`));
      setError('');
    } catch (e) {
      setError(e instanceof Error ? e.message : '점검표를 불러오지 못했습니다.');
    }
  }, [code, params]);
  useEffect(() => { load(); }, [load]);

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
    const missing = sheet.items.filter(i => i.required && !i.result?.result).length;
    if (missing > 0 && !confirm(`아직 입력하지 않은 필수 항목이 ${missing}개 있습니다. 그래도 제출해 볼까요?`)) return;
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
  const groups = ['common', 'zone', 'weekly', 'event'] as const;
  const readOnly = !sheet.canEdit;

  return (
    <div className="ck-zone">
      <header className="ck-zhead">
        <div className="ck-zline">{sheet.line} · {sheet.zoneCode}</div>
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
          {sheet.isCurrent ? '점검할 권한이 없습니다(사용자 관리에서 현장 점검 조회 등급 이상 필요).' : '지금 교대나 바로 앞 교대가 아니라서 입력할 수 없습니다.'}
        </div>
      )}

      {groups.map(g => {
        const list = sheet.items.filter(i => i.group === g);
        if (list.length === 0) return null;
        return (
          <section key={g} className="ck-group">
            <h3>{g === 'zone' ? `${sheet.zoneName} 항목` : GROUP_TITLE[g]}</h3>
            {list.map(item => (
              <ItemCard key={item.itemId} item={item} readOnly={readOnly} busy={!!busy[item.itemId]}
                photoLabel={`${sheet.shift}_${sheet.zoneCode} ${sheet.zoneName}_${item.code}`}
                onSave={d => save(item, d)} onPreview={setPreview} />
            ))}
          </section>
        );
      })}
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

      {preview && (
        <div className="modal-bg" onClick={() => setPreview(null)}>
          <AttImage value={preview} className="ck-preview" />
        </div>
      )}
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

  const done = !!item.doneElsewhere;
  const disabled = readOnly || done || busy;
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
    const refs = await filesToAtts([file], { imagesOnly: true, scope: 'field', cat: '체크시트', label: `${photoLabel}_${PHOTO_LABEL[slot]}` });
    if (refs.length === 0) return;
    onSave({ ...d, memo, photos: [...d.photos.filter(p => p.k !== slot), { k: slot, v: refs[0] }] });
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
          <span className={`ck-tag ${item.dueState === '밀림' ? 'bad' : item.dueState === '오늘' ? 'warn' : ''}`}>
            {item.weekdayLabel ? `${item.weekdayLabel}요일 · ` : ''}{item.dueState}
          </span>
        )}
        {!item.required && item.group !== 'weekly' && <span className="ck-tag">선택</span>}
        {busy && <span className="ck-saving">저장 중…</span>}
      </div>
      <div className="ck-text">{item.text}</div>
      {item.detail && <div className="ck-detail">{item.detail}</div>}
      {item.specText && <div className="ck-spec">기준 {item.specText}</div>}

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
