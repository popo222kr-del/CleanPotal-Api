import { useCallback, useEffect, useState } from 'react';
import { api } from '../../api/client';
import type { CheckNg } from '../../api/types';
import { useAccess } from '../../auth/useAccess';
import AttImage from '../../components/AttImage';
import { dayLabel, PHOTO_LABEL, timeLabel } from './common';

// NG 목록. 1단계는 기록과 "조치 완료" 표시까지 — 담당부서 조치 흐름은 시범 운영 뒤에 붙인다.

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

  const open = list?.filter(n => n.ngStatus === 'OPEN').length ?? 0;

  return (
    <div>
      <div className="ck-toolbar">
        <div className="ck-seg">
          <button className={!all ? 'on' : ''} onClick={() => setAll(false)}>미조치</button>
          <button className={all ? 'on' : ''} onClick={() => setAll(true)}>전체</button>
        </div>
        {list && <span className="ck-muted">{all ? `${list.length}건 (미조치 ${open})` : `${list.length}건`}</span>}
        <button className="ck-iconbtn" onClick={load} title="새로고침">↻</button>
      </div>

      <section className="ck-panel">
        {list === null ? <div className="ck-empty">불러오는 중…</div>
          : list.length === 0 ? <div className="ck-empty">{all ? 'NG 기록이 없습니다.' : '미조치 NG 가 없습니다.'}</div>
          : (
            <table className="ck-table ck-ngtable">
              <thead>
                <tr><th>상태</th><th>근무일</th><th>구역</th><th>항목</th><th>사유 · 측정값</th><th>사진</th><th>점검자</th><th>조치</th></tr>
              </thead>
              <tbody>
                {list.map(ng => (
                  <tr key={ng.resultId} className={ng.ngStatus === 'DONE' ? 'done' : ''}>
                    <td>{ng.ngStatus === 'DONE' ? <span className="ck-pill ok">조치 완료</span> : <span className="ck-pill bad">미조치</span>}</td>
                    <td className="nowrap">{dayLabel(ng.workDate)} <span className="ck-muted">{ng.shift}</span></td>
                    <td className="nowrap">{ng.zoneName}<span className="ck-zcode">{ng.line}</span></td>
                    <td><span className="ck-code">{ng.itemCode}</span> {ng.itemText}</td>
                    <td>
                      {ng.memo || <span className="ck-muted">(사유 없음)</span>}
                      {ng.numValue !== null && <div className="ck-sub">측정 {ng.numValue} · 기준 {ng.specText}</div>}
                    </td>
                    <td>
                      <div className="ck-thumbs">
                        {ng.photos.map(p => (
                          <AttImage key={p.k + p.v} value={p.v} className="ck-mini" onClick={() => setPreview(p.v)} alt={PHOTO_LABEL[p.k]} />
                        ))}
                        {ng.photos.length === 0 && <span className="ck-muted">—</span>}
                      </div>
                    </td>
                    <td className="nowrap">{ng.checkedByName}<div className="ck-sub">{timeLabel(ng.checkedAt)}</div></td>
                    <td>
                      {ng.ngStatus === 'DONE'
                        ? <>{ng.ngCloseNote}<div className="ck-sub">{ng.ngClosedBy} · {timeLabel(ng.ngClosedAt)}</div></>
                        : canEditField
                          ? <button className="ck-btn-sm primary" onClick={() => close(ng)}>조치 완료</button>
                          : <span className="ck-muted">{ng.ngDept ? `담당 ${ng.ngDept}` : '—'}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>

      {preview && (
        <div className="modal-bg" onClick={() => setPreview(null)}>
          <AttImage value={preview} className="ck-preview" />
        </div>
      )}
    </div>
  );
}
