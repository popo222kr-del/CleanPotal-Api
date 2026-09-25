import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { CheckNg } from '../../api/types';
import { useAccess } from '../../auth/useAccess';
import AttImage from '../../components/AttImage';
import { dayLabel, PHOTO_LABEL, timeLabel } from './common';

// 미조치 NG 목록. 1단계는 기록과 "조치 완료" 표시까지 — 담당부서 조치 흐름은 시범 운영 뒤에 붙인다.

export default function NgTab() {
  const { canEditField } = useAccess();
  const [all, setAll] = useState(false);
  const [list, setList] = useState<CheckNg[] | null>(null);
  const [preview, setPreview] = useState<string | null>(null);

  const load = useCallback(async () => {
    try { setList(await api.get<CheckNg[]>(`/api/checklist/ng?open=${!all}`)); }
    catch (e) { alert(e instanceof Error ? e.message : 'NG 목록을 불러오지 못했습니다.'); }
  }, [all]);
  useEffect(() => { load(); }, [load]);

  async function close(ng: CheckNg) {
    const note = prompt(`${ng.zoneName} · ${ng.itemText}\n어떻게 조치했는지 적어 주세요.`)?.trim();
    if (!note) return;
    try {
      const saved = await api.put<CheckNg>(`/api/checklist/ng/${ng.resultId}/close`, { note });
      setList(l => (l ?? []).map(x => x.resultId === saved.resultId ? saved : x).filter(x => all || x.ngStatus === 'OPEN'));
    } catch (e) { alert(e instanceof Error ? e.message : '저장하지 못했습니다.'); }
  }

  return (
    <div>
      <div className="ck-bar-row">
        <label className="ck-check"><input type="checkbox" checked={all} onChange={e => setAll(e.target.checked)} /> 조치 완료 포함</label>
        <button className="btn btn-ghost" onClick={load}>새로고침</button>
      </div>
      {list === null && <div className="ck-empty">불러오는 중…</div>}
      {list?.length === 0 && <div className="ck-empty">{all ? 'NG 기록이 없습니다.' : '미조치 NG 가 없습니다.'}</div>}
      <div className="ck-nglist">
        {list?.map(ng => (
          <div key={ng.resultId} className={`ck-ng ${ng.ngStatus === 'DONE' ? 'done' : ''}`}>
            <div className="ck-nghead">
              <span className="ck-tag">{ng.line}</span>
              <b>{ng.zoneName}</b>
              <span className="ck-dim">{dayLabel(ng.workDate)} {ng.shift}</span>
              {ng.ngStatus === 'DONE' ? <span className="ck-tag okc">조치 완료</span> : <span className="ck-tag bad">미조치</span>}
            </div>
            <div className="ck-text">{ng.itemCode} {ng.itemText}</div>
            {ng.itemDetail && <div className="ck-detail">{ng.itemDetail}</div>}
            {ng.numValue !== null && <div className="ck-spec">측정값 {ng.numValue} (기준 {ng.specText})</div>}
            <div className="ck-ngmemo">사유: {ng.memo || '(없음)'}</div>
            {ng.photos.length > 0 && (
              <div className="ck-ngphotos">
                {ng.photos.map(p => (
                  <figure key={p.k + p.v}>
                    <AttImage value={p.v} className="ck-thumb" onClick={() => setPreview(p.v)} />
                    <figcaption>{PHOTO_LABEL[p.k]}</figcaption>
                  </figure>
                ))}
              </div>
            )}
            <div className="ck-who">
              점검 {ng.checkedByName} · {timeLabel(ng.checkedAt)}{ng.ngDept && <> · 담당 {ng.ngDept}</>}
            </div>
            {ng.ngStatus === 'DONE'
              ? <div className="ck-ngdone">조치: {ng.ngCloseNote} — {ng.ngClosedBy} {timeLabel(ng.ngClosedAt)}</div>
              : canEditField && <button className="btn btn-primary ck-ngclose" onClick={() => close(ng)}>조치 완료</button>}
          </div>
        ))}
      </div>
      {preview && (
        <div className="modal-bg" onClick={() => setPreview(null)}>
          <AttImage value={preview} className="ck-preview" />
        </div>
      )}
    </div>
  );
}
