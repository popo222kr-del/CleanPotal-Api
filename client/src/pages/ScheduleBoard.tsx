import { useEffect, useState, useCallback, useRef } from 'react';
import html2canvas from 'html2canvas';
import { api } from '../api/client';
import type { ScheduleBlock, ScheduleRecipe, ScheduleEquipment, ShiftTeams } from '../api/types';
import './ScheduleBoard.css';

// ── 상수 (WPF ScheduleBoardViewModel) ──
const START_HOUR = 7;
const TOTAL_MIN = 24 * 60;          // 07:00 ~ 익일 07:00
const MIN_PER_CELL = 10;
const TOTAL_CELLS = TOTAL_MIN / MIN_PER_CELL;   // 144
const MAX_DI = 5;                   // DI 동시 배치 제한
const BASE_CELL_W = 20;
const BASE_ROW_H = 26;
const EQUIP_W = 220;
const DOW = ['일', '월', '화', '수', '목', '금', '토'];

const COL = { s2: '#F87171', hf: '#FABF24', di: '#38BDF8' };

// 로컬 편집용 블록 (id 없음 — 저장 시 전량 교체)
type Block = {
  key: string; equipmentIndex: number; startMinute: number;
  s2: number; hf: number; di: number; temp: number | null; recipeText: string;
};
let keySeq = 1;
const nk = () => `b${keySeq++}`;

function ymd(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
function addDays(s: string, n: number): string {
  const d = new Date(s + 'T00:00:00'); d.setDate(d.getDate() + n); return ymd(d);
}
function dateTitle(s: string): string {
  const d = new Date(s + 'T00:00:00');
  return `${s} (${DOW[d.getDay()]}요일)`;
}
function blockTotal(b: Block): number { return b.s2 + b.hf + b.di; }
function recipeTotal(r: ScheduleRecipe): number { return r.s2Minutes + r.hfMinutes + r.diMinutes; }
function blockLabel(b: Block): string {
  const base = b.recipeText.split('@')[0];
  return b.temp != null ? `${base} (S2 ${b.temp}℃ ${b.s2}분)` : base;
}
function absTime(minOffset: number): string {
  const abs = START_HOUR * 60 + minOffset;
  const p = (n: number) => String(n).padStart(2, '0');
  return `${p(Math.floor(abs / 60) % 24)}:${p(abs % 60)}`;
}

// 링(1440분) 상의 두 구간이 겹치는지
function overlapCircular(s1: number, l1: number, s2: number, l2: number): boolean {
  for (let i = 0; i < l1; i++) {
    const c1 = (s1 + i) % TOTAL_MIN;
    for (let j = 0; j < l2; j++) if (c1 === (s2 + j) % TOTAL_MIN) return true;
  }
  return false;
}

export default function ScheduleBoard() {
  const [date, setDate] = useState(() => ymd(new Date()));
  const [equipments, setEquipments] = useState<ScheduleEquipment[]>([]);
  const [recipes, setRecipes] = useState<ScheduleRecipe[]>([]);
  const [blocks, setBlocks] = useState<Block[]>([]);
  const [selRecipe, setSelRecipe] = useState<ScheduleRecipe | null>(null);
  const [zoom, setZoom] = useState(1);
  const [status, setStatus] = useState('');
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);
  const [mgrOpen, setMgrOpen] = useState(false);              // 설비 & 레시피 관리 (통합)
  const [mgrTab, setMgrTab] = useState<'equip' | 'recipe'>('equip');
  const [newS2, setNewS2] = useState('');        // 레시피 등록: 구조화 입력 (빈칸 시작)
  const [newHf, setNewHf] = useState('');
  const [newDi, setNewDi] = useState('');
  const [newHot, setNewHot] = useState(false);   // Hot Chemical → S2 온도 사용
  const [newTemp, setNewTemp] = useState(60);    // Hot Chemical 온도(기본 60℃, 편집 가능)
  const [newEquipName, setNewEquipName] = useState('');
  const [newEquipProcess, setNewEquipProcess] = useState('');
  const [newEquipNote, setNewEquipNote] = useState('');
  const [newEquipGroup, setNewEquipGroup] = useState('MDC');
  const [editRec, setEditRec] = useState<ScheduleRecipe | null>(null);   // 수정 중인 레시피
  const [shift, setShift] = useState<'day' | 'night'>('day');   // 주간/야간 보기 (클릭 시 해당 시작 시각으로 이동)
  const [shiftTeams, setShiftTeams] = useState<ShiftTeams | null>(null);   // 해당 날짜 주간/야간 근무팀
  const [nowTs, setNowTs] = useState(() => Date.now());          // 현재 시각선 갱신용(1분)
  const gridRef = useRef<HTMLDivElement>(null);
  const [fitRowH, setFitRowH] = useState(0);          // 화면을 꽉 채우는 행 높이
  const undoRef = useRef<Block[][]>([]);
  const captureRef = useRef<HTMLDivElement>(null);
  const loadSeq = useRef(0);
  // 호버 인디케이터 (성능 위해 setState 없이 직접 스타일 갱신 — WPF hover)
  const hvRowRef = useRef<HTMLDivElement>(null);
  const hvColRef = useRef<HTMLDivElement>(null);
  const hvCellRef = useRef<HTMLDivElement>(null);
  const hvEquipRef = useRef<HTMLDivElement>(null);
  const hvTextRef = useRef<HTMLSpanElement>(null);
  // 캡처 모드: range=현재 화면 / multi=선택한 여러 날짜
  const [capMode, setCapMode] = useState<'range' | 'multi'>('range');
  // 멀티 캡처: 달력에서 날짜 여러 개 선택 + 주간/야간 범위 선택
  const [multiOpen, setMultiOpen] = useState(false);
  const [multiDates, setMultiDates] = useState<Set<string>>(new Set());
  const [multiMonth, setMultiMonth] = useState(() => ymd(new Date()).slice(0, 7));   // yyyy-MM
  const [multiRange, setMultiRange] = useState<'day' | 'night' | 'all'>('day');
  const [multiData, setMultiData] = useState<{ date: string; blocks: Block[] }[] | null>(null);

  const cellW = Math.max(1, Math.round(BASE_CELL_W * zoom));
  const rowH = Math.max(Math.max(1, Math.round(BASE_ROW_H * zoom)), fitRowH);
  const boardW = TOTAL_CELLS * cellW;
  const bodyH = equipments.length * rowH;
  // 블록은 설비 배열 위치가 아니라 안정적 index(Slot)로 참조 → 재정렬/삭제해도 안 어긋남
  const eqName = (idx: number) => equipments.find(e => e.index === idx)?.displayName ?? '';
  const rowByIndex = new Map(equipments.map((e, i) => [e.index, i]));

  // 설비·레시피 (최초 1회)
  useEffect(() => {
    api.get<ScheduleEquipment[]>('/api/scheduleboard/equipments').then(setEquipments).catch(() => {});
    loadRecipes();
  }, []);
  const loadRecipes = () => api.get<ScheduleRecipe[]>('/api/scheduleboard/recipes').then(setRecipes).catch(() => {});

  // 해당 날짜 주간/야간 근무팀 (툴바 배지)
  useEffect(() => {
    api.get<ShiftTeams>(`/api/schedule/shift-teams?date=${date}`).then(setShiftTeams).catch(() => setShiftTeams(null));
  }, [date]);

  // 현재 시각선: 1분마다 갱신
  useEffect(() => {
    const t = setInterval(() => setNowTs(Date.now()), 60000);
    return () => clearInterval(t);
  }, []);

  const toBlock = (d: ScheduleBlock): Block => ({
    key: nk(), equipmentIndex: d.equipmentIndex, startMinute: d.startMinute,
    s2: d.s2Minutes, hf: d.hfMinutes, di: d.diMinutes, temp: d.s2Temperature, recipeText: d.recipeText,
  });

  const loadDay = useCallback(async (target: string) => {
    const seq = ++loadSeq.current;
    try {
      const list = await api.get<ScheduleBlock[]>(`/api/scheduleboard/day?date=${target}`);
      if (seq !== loadSeq.current) return;
      setBlocks(list.map(toBlock));
      undoRef.current = [];
      setDirty(false);
      setStatus(`${dateTitle(target)} 불러옴 (${list.length}건)`);
    } catch {
      if (seq !== loadSeq.current) return;
      setBlocks([]); setDirty(false); setStatus('불러오기 실패');
    }
  }, []);
  useEffect(() => { loadDay(date); }, [date, loadDay]);

  useEffect(() => {
    const h = (e: BeforeUnloadEvent) => { if (dirty) { e.preventDefault(); e.returnValue = ''; } };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);

  // 아래 여백 없이 + 페이지 세로 스크롤 없이: 설비 행 높이를 정확히 화면에 맞춤
  const [bodyMaxH, setBodyMaxH] = useState(0);
  useEffect(() => {
    const calc = () => {
      const el = gridRef.current;
      if (!el || equipments.length === 0) return;
      // 그리드 위쪽 위치 기준 남은 높이 - 시간축(34) - 카드/페이지 하단 패딩(30)
      const avail = window.innerHeight - el.getBoundingClientRect().top - 48 - 30;
      setBodyMaxH(avail);
      // 가로 스크롤바(17px)가 보드 안에서 차지하는 높이까지 뺀 뒤 행 높이 산출
      setFitRowH(Math.floor((avail - 17) / equipments.length));
    };
    calc();
    window.addEventListener('resize', calc);
    return () => window.removeEventListener('resize', calc);
  }, [equipments.length]);

  // 주간/야간 버튼 → 해당 시작 시각(07:00/19:00)으로 스크롤 이동
  function selectShift(v: 'day' | 'night') {
    setShift(v);
    const el = document.getElementById('sb-board-scroll');
    if (el) el.scrollLeft = ((v === 'night' ? 720 : 0) / MIN_PER_CELL) * cellW;
  }

  function changeDate(next: string) {
    if (dirty && !confirm('저장하지 않은 변경이 있습니다. 날짜를 이동할까요?')) return;
    setDate(next);
  }
  function pushUndo() { undoRef.current.push(blocks.map(b => ({ ...b }))); }
  function undo() {
    const prev = undoRef.current.pop();
    if (!prev) { setStatus('되돌릴 작업이 없습니다.'); return; }
    setBlocks(prev); setDirty(true); setStatus('되돌리기 완료');
  }

  // ── 배치 (좌클릭) ──
  function placeAt(equipmentIndex: number, startMinute: number) {
    if (!selRecipe) { setStatus(`시간 선택: ${eqName(equipmentIndex)} / ${absTime(startMinute)}`); return; }
    const total = recipeTotal(selRecipe);
    if (total > TOTAL_MIN) { setStatus('배치 불가: 레시피 길이가 24시간을 초과합니다.'); return; }
    // 같은 설비 겹침
    const overlap = blocks.some(b => b.equipmentIndex === equipmentIndex &&
      overlapCircular(startMinute, total, b.startMinute, blockTotal(b)));
    if (overlap) { setStatus(`배치 불가: ${eqName(equipmentIndex)} 행에 겹치는 레시피가 있습니다.`); return; }
    // DI 동시 5개 제한
    if (selRecipe.diMinutes > 0) {
      const diStart = startMinute + selRecipe.s2Minutes + selRecipe.hfMinutes;
      for (let i = 0; i < selRecipe.diMinutes; i++) {
        const m = (diStart + i) % TOTAL_MIN;
        let concurrent = 1;
        for (const b of blocks) {
          if (b.di <= 0) continue;
          const bStart = b.startMinute + b.s2 + b.hf;
          for (let j = 0; j < b.di; j++) if ((bStart + j) % TOTAL_MIN === m) { concurrent++; break; }
        }
        if (concurrent > MAX_DI) { setStatus(`배치 불가: DI 동시 배치 ${MAX_DI}개 제한 초과 (${absTime(m)} 구간)`); return; }
      }
    }
    pushUndo();
    setBlocks(prev => [...prev, {
      key: nk(), equipmentIndex, startMinute,
      s2: selRecipe.s2Minutes, hf: selRecipe.hfMinutes, di: selRecipe.diMinutes,
      temp: selRecipe.s2Temperature, recipeText: selRecipe.text,
    }]);
    setDirty(true);
    setStatus(`적용: ${eqName(equipmentIndex)} / ${selRecipe.text}`);
  }
  // ── 삭제 (우클릭) ──
  function removeAt(equipmentIndex: number, clickMinute: number) {
    const target = blocks.find(b => {
      if (b.equipmentIndex !== equipmentIndex) return false;
      for (let i = 0; i < blockTotal(b); i++) if ((b.startMinute + i) % TOTAL_MIN === clickMinute) return true;
      return false;
    });
    if (!target) { setStatus('해당 시간에 삭제할 레시피가 없습니다.'); return; }
    pushUndo();
    setBlocks(prev => prev.filter(b => b.key !== target.key));
    setDirty(true);
    setStatus(`삭제: ${eqName(equipmentIndex)} / ${target.recipeText}`);
  }
  // 보드 클릭 좌표 → (행, 분)
  function cellFromEvent(e: React.MouseEvent, el: HTMLDivElement): { row: number; minute: number } | null {
    const rect = el.getBoundingClientRect();
    const x = e.clientX - rect.left, y = e.clientY - rect.top;
    const row = Math.floor(y / rowH);
    const minute = Math.round((x / cellW) * MIN_PER_CELL);
    if (row < 0 || row >= equipments.length || minute < 0 || minute >= TOTAL_MIN) return null;
    return { row, minute };
  }

  async function save() {
    setSaving(true);
    const seq = ++loadSeq.current;   // 저장 중 날짜가 바뀌면 이 응답은 무시
    try {
      const body = { blocks: blocks.map(b => ({
        equipmentIndex: b.equipmentIndex, startMinute: b.startMinute,
        s2Minutes: b.s2, hfMinutes: b.hf, diMinutes: b.di, s2Temperature: b.temp, recipeText: b.recipeText,
      })) };
      const saved = await api.put<ScheduleBlock[]>(`/api/scheduleboard/day?date=${date}`, body);
      if (seq !== loadSeq.current) return;   // 다른 날짜로 이동함 → 화면 덮어쓰지 않음
      setBlocks(saved.map(toBlock));
      undoRef.current = []; setDirty(false);
      setStatus(`저장 완료 (${saved.length}건)`);
    } catch (e) {
      setStatus(`저장 실패: ${e instanceof Error ? e.message : e}`);
    } finally { setSaving(false); }
  }

  // 레시피 관리
  async function addRecipe() {
    const p = (v: string) => v.trim() === '' ? NaN : Number(v);
    const s2 = p(newS2), hf = p(newHf), di = p(newDi), temp = newTemp | 0;
    if ([s2, hf, di].some(n => isNaN(n) || n < 0)) { alert('S2·HF·DI를 0 이상 숫자로 입력하세요.'); return; }
    if (temp < 0) { alert('온도는 음수일 수 없습니다.'); return; }
    const text = newHot ? `${s2}-${hf}-${di}@${temp}` : `${s2}-${hf}-${di}`;   // Hot Chemical → S2 온도
    try {
      await api.post('/api/scheduleboard/recipes', { text });
      setNewS2(''); setNewHf(''); setNewDi(''); setNewHot(false); setNewTemp(60);
      await loadRecipes(); setStatus(`레시피 추가: ${text}`);
    }
    catch (e) { alert(e instanceof Error ? e.message : '추가 실패'); }
  }
  async function delRecipe(r: ScheduleRecipe) {
    if (blocks.some(b => b.recipeText === r.text)) { alert('현재 날짜에 배치된 레시피는 삭제할 수 없습니다.'); return; }
    if (!confirm(`레시피 삭제: ${r.text}?`)) return;
    await api.del(`/api/scheduleboard/recipes/${r.id}`);
    if (selRecipe?.id === r.id) setSelRecipe(null);
    loadRecipes();
  }
  async function toggleFav(r: ScheduleRecipe) {
    await api.patch(`/api/scheduleboard/recipes/${r.id}/favorite`, { favorite: !r.isFavorite });
    loadRecipes();
  }
  async function saveRecipeEdit() {
    if (!editRec) return;
    try {
      await api.put(`/api/scheduleboard/recipes/${editRec.id}`, {
        s2Minutes: editRec.s2Minutes, hfMinutes: editRec.hfMinutes, diMinutes: editRec.diMinutes, s2Temperature: editRec.s2Temperature,
      });
      setEditRec(null); await loadRecipes(); setStatus('레시피 수정 완료');
    } catch (e) { alert(e instanceof Error ? e.message : '수정 실패'); }
  }

  // 설비 관리
  const loadEquip = () => api.get<ScheduleEquipment[]>('/api/scheduleboard/equipments').then(setEquipments).catch(() => {});
  async function addEquip() {
    const name = newEquipName.trim(); if (!name) return;
    await api.post('/api/scheduleboard/equipments', {
      name, groupName: newEquipGroup,
      process: newEquipProcess.trim(), note: newEquipNote.trim(), isIdle: false,
    });
    setNewEquipName(''); setNewEquipProcess(''); setNewEquipNote(''); loadEquip();
  }
  // 부분 수정: 넘긴 필드만 덮어쓰고 나머지는 기존값 유지
  async function saveEquip(e: ScheduleEquipment, patch: Partial<Pick<ScheduleEquipment, 'name' | 'groupName' | 'process' | 'note' | 'isIdle'>>) {
    await api.put(`/api/scheduleboard/equipments/${e.id}`, {
      name: patch.name ?? e.name,
      groupName: patch.groupName ?? e.groupName,
      process: patch.process ?? e.process,
      note: patch.note ?? e.note,
      isIdle: patch.isIdle ?? e.isIdle,
    });
    loadEquip();
  }
  async function delEquip(e: ScheduleEquipment) {
    if (!confirm(`설비 삭제: ${e.displayName}?\n(기존 배치는 보존되며 목록에서만 숨겨집니다)`)) return;
    await api.del(`/api/scheduleboard/equipments/${e.id}`); loadEquip();
  }
  async function moveEquip(idx: number, dir: -1 | 1) {
    const arr = [...equipments]; const j = idx + dir;
    if (j < 0 || j >= arr.length) return;
    [arr[idx], arr[j]] = [arr[j], arr[idx]];
    setEquipments(arr);
    await api.post('/api/scheduleboard/equipments/reorder', { ids: arr.map(x => x.id) }); loadEquip();
  }

  const [capturing, setCapturing] = useState(false);

  // 캡처 공통: 숨김 캡처 뷰 → 이미지 → 클립보드(실패 시 다운로드)
  async function snapCaptureView(label: string, fileSuffix: string) {
    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
    await new Promise(r => setTimeout(r, 60));
    if (!captureRef.current) throw new Error('캡처 대상 없음');
    const canvas = await html2canvas(captureRef.current, { backgroundColor: '#ffffff', scale: 1.5 });
    const blob: Blob | null = await new Promise(res => canvas.toBlob(res, 'image/png'));
    if (!blob) throw new Error('이미지 생성 실패');
    try {
      await navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]);
      setStatus(`${label} 클립보드 복사됨`);
      alert(`${label} 이미지가 클립보드에 복사되었습니다.\n메신저에 붙여넣기(Ctrl+V) 하세요.`);
    } catch {
      const a = document.createElement('a');
      a.href = canvas.toDataURL('image/png');
      a.download = `스케줄보드_${fileSuffix}.png`;
      a.click();
      alert(`클립보드 접근이 불가하여 이미지 파일로 다운로드했습니다.\n(${a.download})`);
    }
  }

  async function doCapture(mode: 'range') {
    if (capturing) return;
    setCapturing(true);
    setCapMode(mode);
    try { await snapCaptureView('화면 캡처', date); }
    catch (e) { setStatus('캡처 실패'); alert(`캡처 실패: ${e instanceof Error ? e.message : e}`); }
    finally { setCapMode('range'); setCapturing(false); }
  }

  // ── 멀티 캡처: 선택한 날짜들의 보드를 각각 불러와 세로로 쌓아 한 장으로 ──
  function toggleMultiDate(d: string) {
    setMultiDates(prev => { const n = new Set(prev); if (n.has(d)) n.delete(d); else n.add(d); return n; });
  }
  async function runMultiCapture() {
    const dates = [...multiDates].sort();
    if (dates.length === 0) { alert('캡처할 날짜를 선택해 주세요.'); return; }
    if (dates.length > 10) { alert('한 번에 최대 10일까지 캡처할 수 있습니다.'); return; }
    if (capturing) return;
    setCapturing(true);
    try {
      const data = await Promise.all(dates.map(async d => ({
        date: d,
        blocks: (await api.get<ScheduleBlock[]>(`/api/scheduleboard/day?date=${d}`)).map(toBlock),
      })));
      setMultiData(data);
      setCapMode('multi');
      const rangeLabel = multiRange === 'day' ? '주간' : multiRange === 'night' ? '야간' : '전체';
      await snapCaptureView(`멀티 캡처 (${dates.length}일·${rangeLabel})`, `멀티_${dates[0]}~${dates[dates.length - 1]}`);
      setMultiOpen(false);
      setMultiDates(new Set());
    } catch (e) {
      alert(`멀티 캡처 실패: ${e instanceof Error ? e.message : e}`);
    } finally {
      setCapMode('range');
      setMultiData(null);
      setCapturing(false);
    }
  }

  // ── 호버 인디케이터 (WPF: 행/1분 세로선/셀 박스/설비 하이라이트 + 상태 표시) ──
  function hideHover() {
    for (const r of [hvRowRef, hvColRef, hvCellRef, hvEquipRef]) if (r.current) r.current.style.display = 'none';
    if (hvTextRef.current) hvTextRef.current.textContent = '';
  }
  function boardMouseMove(e: React.MouseEvent<HTMLDivElement>) {
    if (e.ctrlKey) { hideHover(); return; }   // WPF: Ctrl 누르면 호버 숨김
    const c = cellFromEvent(e, e.currentTarget);
    if (!c) { hideHover(); return; }
    const { row, minute } = c;
    const rEl = hvRowRef.current, colEl = hvColRef.current, cellEl = hvCellRef.current, eqEl = hvEquipRef.current;
    if (rEl) { rEl.style.top = `${row * rowH}px`; rEl.style.height = `${rowH}px`; rEl.style.width = `${boardW}px`; rEl.style.display = 'block'; }
    if (colEl) { colEl.style.left = `${(minute / MIN_PER_CELL) * cellW}px`; colEl.style.width = `${Math.max(1, cellW / MIN_PER_CELL)}px`; colEl.style.height = `${bodyH}px`; colEl.style.display = 'block'; }
    if (cellEl) { cellEl.style.left = `${Math.floor(minute / MIN_PER_CELL) * cellW}px`; cellEl.style.top = `${row * rowH + 2}px`; cellEl.style.width = `${cellW}px`; cellEl.style.height = `${Math.max(2, rowH - 4)}px`; cellEl.style.display = 'block'; }
    if (eqEl) { eqEl.style.top = `${row * rowH}px`; eqEl.style.height = `${rowH}px`; eqEl.style.display = 'block'; }
    if (hvTextRef.current) hvTextRef.current.textContent = ` · ${equipments[row]?.displayName ?? ''} | ${absTime(minute)}`;
  }

  const hourLines = Array.from({ length: 25 }, (_, h) => h);   // 07..07 (25개 경계)

  // 현재 시각선 (오늘 날짜일 때만): 07:00 기준 분 오프셋
  const nowOffset = (() => {
    if (date !== ymd(new Date(nowTs))) return null;
    const d = new Date(nowTs);
    let mins = (d.getHours() - START_HOUR) * 60 + d.getMinutes();
    if (mins < 0) mins += TOTAL_MIN;
    return mins >= 0 && mins <= TOTAL_MIN ? mins : null;
  })();
  // 그룹(MDC/MSC/NDC) 경계: 앞 행과 그룹이 다르면 구분선 표시
  const isGrpTop = (i: number) => i > 0 && equipments[i - 1].groupName !== equipments[i].groupName;

  return (
    <div className="sb-page">
      <header className="pg-header">
        <div><h2>스케줄보드</h2></div>
        <button className="btn btn-ghost" onClick={() => setMgrOpen(true)}>설비 &amp; 레시피 관리</button>
        <button className="btn btn-ghost" onClick={() => doCapture('range')} disabled={capturing}>화면 캡처</button>
        <button className="btn btn-ghost" onClick={() => { setMultiMonth(date.slice(0, 7)); setMultiOpen(true); }} disabled={capturing}>멀티 캡처</button>
        <button className="btn btn-ghost" onClick={undo}>되돌리기</button>
        <button className="btn btn-primary" onClick={save} disabled={saving || !dirty}>{saving ? '저장 중…' : dirty ? '저장 *' : '저장됨'}</button>
      </header>

      <div className="sb-body">
        {/* 좌: 레시피 */}
        <aside className="sb-recipes">
          <h3>레시피 선택</h3>
          <div className="sb-recipe-list">
            {recipes.map(r => (
              <div key={r.id} className={`sb-recipe ${selRecipe?.id === r.id ? 'on' : ''}`}>
                <button className={`sb-star ${r.isFavorite ? 'on' : ''}`} onClick={() => toggleFav(r)} title="즐겨찾기">{r.isFavorite ? '★' : '☆'}</button>
                <button className="sb-recipe-btn" onClick={() => setSelRecipe(selRecipe?.id === r.id ? null : r)}>{r.displayText}</button>
              </div>
            ))}
            {recipes.length === 0 && <p className="sb-dim">레시피가 없습니다</p>}
          </div>
        </aside>

        {/* 우: 보드 */}
        <section className="sb-main">
          <div className="sb-toolbar">
            <button className="sb-nav" onClick={() => changeDate(addDays(date, -1))}>◀</button>
            <button className="sb-nav" onClick={() => changeDate(ymd(new Date()))}>오늘</button>
            <button className="sb-nav" onClick={() => changeDate(addDays(date, 1))}>▶</button>
            <input className="sb-date" type="date" value={date} onChange={e => e.target.value && changeDate(e.target.value)} />
            <b className="sb-date-title">{dateTitle(date)}</b>
            <button className={`sb-shift ${shift === 'day' ? 'on' : ''}`} onClick={() => selectShift('day')}>주간{shiftTeams?.dayTeams.length ? ` ${shiftTeams.dayTeams.join(', ')}` : ''} <small>07:00~19:00</small></button>
            <button className={`sb-shift night ${shift === 'night' ? 'on' : ''}`} onClick={() => selectShift('night')}>야간{shiftTeams?.nightTeams.length ? ` ${shiftTeams.nightTeams.join(', ')}` : ''} <small>19:00~06:00</small></button>
            <span className="sb-sel">선택 레시피: {selRecipe ? selRecipe.text : '없음'}</span>
            <div className="sb-zoom">
              <span>Zoom</span>
              <input type="range" min={0.5} max={2} step={0.1} value={zoom} onChange={e => setZoom(+e.target.value)} />
              <span>{Math.round(zoom * 100)}%</span>
            </div>
          </div>
          <div className="sb-hint">클릭 = 선택 레시피 배치 · 우클릭 = 삭제 · {status}<span ref={hvTextRef} className="sb-hover-txt" /></div>

          <div className="sb-grid-wrap" ref={gridRef}>
            {/* 헤더: 설비 + 시간축 */}
            <div className="sb-corner" style={{ width: EQUIP_W }}>설비 목록</div>
            <div className="sb-time-scroll" id="sb-time-scroll">
              <div className="sb-time-axis" style={{ width: boardW }}>
                {hourLines.map(h => {
                  const hour = (START_HOUR + h) % 24;
                  return <div key={h} className="sb-hour" style={{ left: h * 6 * cellW, width: 6 * cellW }}>{String(hour).padStart(2, '0')}:00</div>;
                })}
                {Array.from({ length: TOTAL_CELLS }, (_, c) => {
                  const m = (c * MIN_PER_CELL) % 60;
                  return m === 0 ? null : <div key={`m${c}`} className="sb-min" style={{ left: c * cellW, width: cellW }}>{m}</div>;
                })}
              </div>
            </div>

            {/* 본문: 설비 열 + 보드 */}
            <div className="sb-equip-col" id="sb-equip-col" style={{ width: EQUIP_W, maxHeight: bodyMaxH || undefined, position: 'relative' }}>
              {equipments.map((eq, i) => (
                <div key={eq.index} className={`sb-equip ${eq.groupName === 'MDC' ? 'mdc' : eq.groupName === 'NDC' ? 'ndc' : ''} ${eq.isIdle ? 'idle' : ''} ${isGrpTop(i) ? 'grp-top' : ''}`}
                  style={{ height: rowH }}>
                  <span className="sb-eq-txt">{eq.displayName}</span>
                  {eq.isIdle && <span className="sb-idle-pill">유휴</span>}
                </div>
              ))}
              <div ref={hvEquipRef} className="sb-hv-equip" style={{ width: EQUIP_W }} />
            </div>
            <div className="sb-board-scroll" id="sb-board-scroll" style={{ maxHeight: bodyMaxH || undefined }} onScroll={e => {
              const el = e.target as HTMLElement;
              const ts = document.getElementById('sb-time-scroll'); if (ts) ts.scrollLeft = el.scrollLeft;
              const ec = document.getElementById('sb-equip-col'); if (ec) ec.scrollTop = el.scrollTop;
            }}>
              <div className="sb-board" style={{ width: boardW, height: bodyH }}
                onClick={e => { const c = cellFromEvent(e, e.currentTarget); if (!c) return; const idx = equipments[c.row]?.index; if (idx !== undefined) placeAt(idx, c.minute); }}
                onContextMenu={e => { e.preventDefault(); const c = cellFromEvent(e, e.currentTarget); if (!c) return; const idx = equipments[c.row]?.index; if (idx !== undefined) removeAt(idx, c.minute); }}
                onMouseMove={boardMouseMove}
                onMouseLeave={hideHover}>
                {/* 행 배경 */}
                {equipments.map((eq, i) => (
                  <div key={eq.index} className={`sb-row ${eq.groupName === 'MDC' ? 'mdc' : eq.groupName === 'NDC' ? 'ndc' : ''} ${eq.isIdle ? 'idle' : ''} ${isGrpTop(i) ? 'grp-top' : ''}`}
                    style={{ top: i * rowH, height: rowH, width: boardW }} />
                ))}
                {/* 시간 세로선 */}
                {hourLines.map(h => <div key={h} className="sb-vline" style={{ left: h * 6 * cellW, height: bodyH }} />)}
                {/* 현재 시각선 (오늘) */}
                {nowOffset != null && <div className="sb-now" style={{ left: (nowOffset / MIN_PER_CELL) * cellW, height: bodyH }} />}
                {/* 블록 */}
                {blocks.filter(b => rowByIndex.has(b.equipmentIndex)).map(b => <BlockView key={b.key} b={b} row={rowByIndex.get(b.equipmentIndex)!} cellW={cellW} rowH={rowH} boardW={boardW} zoom={zoom} />)}
                {/* 호버 인디케이터 (마우스 이벤트 통과) */}
                <div ref={hvRowRef} className="sb-hv-row" />
                <div ref={hvColRef} className="sb-hv-col" />
                <div ref={hvCellRef} className="sb-hv-cell" />
              </div>
            </div>
            {blocks.length === 0 && <div className="sb-empty">레시피를 선택한 뒤, 배치할 설비 행의 시간대를 클릭하세요</div>}
          </div>
        </section>
      </div>

      {/* 캡처 전용 (현재 보드 복제) */}
      <div className="sb-capture-holder" aria-hidden>
        <div ref={captureRef} className="sb-capture">
          {(() => {
            const equipCol = (
              <div className="sb-cap-equip" style={{ width: EQUIP_W }}>
                <div className="sb-cap-eqhead" style={{ height: 48 }}>설비</div>
                {equipments.map(eq => <div key={eq.index} className={`sb-equip ${eq.groupName === 'MDC' ? 'mdc' : eq.groupName === 'NDC' ? 'ndc' : ''} ${eq.isIdle ? 'idle' : ''}`} style={{ height: rowH }}><span className="sb-eq-txt">{eq.displayName}</span>{eq.isIdle && <span className="sb-idle-pill">유휴</span>}</div>)}
              </div>
            );
            const band = (capStart: number, capEnd: number, blks: Block[] = blocks) => {
              const capW = ((capEnd - capStart) / MIN_PER_CELL) * cellW;
              const off = (capStart / MIN_PER_CELL) * cellW;
              return (
                <div style={{ position: 'relative', width: capW, overflow: 'hidden' }}>
                  <div style={{ width: boardW, marginLeft: -off }}>
                    <div className="sb-cap-axis" style={{ width: boardW, height: 48, position: 'relative' }}>
                      {hourLines.map(h => <div key={h} className="sb-hour" style={{ left: h * 6 * cellW, width: 6 * cellW }}>{String((START_HOUR + h) % 24).padStart(2, '0')}:00</div>)}
                      {Array.from({ length: TOTAL_CELLS }, (_, c) => {
                        const m = (c * MIN_PER_CELL) % 60;
                        return m === 0 ? null : <div key={`m${c}`} className="sb-min" style={{ left: c * cellW, width: cellW }}>{m}</div>;
                      })}
                    </div>
                    <div className="sb-board" style={{ width: boardW, height: bodyH }}>
                      {equipments.map((eq, i) => <div key={eq.index} className={`sb-row ${eq.groupName === 'MDC' ? 'mdc' : eq.groupName === 'NDC' ? 'ndc' : ''}`} style={{ top: i * rowH, height: rowH, width: boardW }} />)}
                      {hourLines.map(h => <div key={h} className="sb-vline" style={{ left: h * 6 * cellW, height: bodyH }} />)}
                      {blks.filter(b => rowByIndex.has(b.equipmentIndex)).map(b => <BlockView key={b.key} b={b} row={rowByIndex.get(b.equipmentIndex)!} cellW={cellW} rowH={rowH} boardW={boardW} zoom={zoom} />)}
                    </div>
                  </div>
                </div>
              );
            };
            if (capMode === 'multi' && multiData) {
              const [mrs, mre] = multiRange === 'day' ? [0, 720] : multiRange === 'night' ? [720, TOTAL_MIN] : [0, TOTAL_MIN];
              const rangeLabel = multiRange === 'day' ? '주간 07:00~19:00' : multiRange === 'night' ? '야간 19:00~06:00' : '전체 07:00~06:00';
              return (
                <>
                  <h2>스케줄보드 멀티 캡처 ({rangeLabel})</h2>
                  {multiData.map(md => (
                    <div key={md.date}>
                      <div className="sb-cap-label day">{dateTitle(md.date)}</div>
                      <div className="sb-cap-grid">{equipCol}{band(mrs, mre, md.blocks)}</div>
                    </div>
                  ))}
                </>
              );
            }
            const rs = shift === 'night' ? 720 : 0;
            const re = shift === 'night' ? TOTAL_MIN : 720;
            return (
              <>
                <h2>{dateTitle(date)} 스케줄보드 {shift === 'night' ? '(야간 19:00~06:00)' : '(주간 07:00~19:00)'}</h2>
                <div className="sb-cap-grid">{equipCol}{band(rs, re)}</div>
              </>
            );
          })()}
        </div>
      </div>

      {/* 멀티 캡처 모달: 달력에서 날짜 선택 + 주간/야간 범위 */}
      {multiOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget && !capturing) setMultiOpen(false); }}>
          <div className="modal-box sb-multi">
            <h3>멀티 캡처</h3>
            <div className="sb-multi-range">
              {([['day', '주간 07:00~19:00'], ['night', '야간 19:00~06:00'], ['all', '전체 07:00~06:00']] as const).map(([k, lbl]) => (
                <button key={k} type="button" className={`sb-shift ${k === 'night' ? 'night' : ''} ${multiRange === k ? 'on' : ''}`}
                  onClick={() => setMultiRange(k)}>{lbl}</button>
              ))}
            </div>
            {(() => {
              const [y, m] = multiMonth.split('-').map(Number);
              const first = new Date(y, m - 1, 1);
              const daysInMonth = new Date(y, m, 0).getDate();
              const lead = first.getDay();
              const prevM = () => setMultiMonth(ymd(new Date(y, m - 2, 1)).slice(0, 7));
              const nextM = () => setMultiMonth(ymd(new Date(y, m, 1)).slice(0, 7));
              const today = ymd(new Date());
              return (
                <div className="sb-cal">
                  <div className="sb-cal-head">
                    <button type="button" className="sb-nav" onClick={prevM}>◀</button>
                    <b>{y}년 {m}월</b>
                    <button type="button" className="sb-nav" onClick={nextM}>▶</button>
                  </div>
                  <div className="sb-cal-grid">
                    {DOW.map(d => <div key={d} className={`sb-cal-dow ${d === '일' ? 'sun' : d === '토' ? 'sat' : ''}`}>{d}</div>)}
                    {Array.from({ length: lead }, (_, i) => <div key={`e${i}`} />)}
                    {Array.from({ length: daysInMonth }, (_, i) => {
                      const dstr = `${multiMonth}-${String(i + 1).padStart(2, '0')}`;
                      const on = multiDates.has(dstr);
                      return (
                        <button key={dstr} type="button"
                          className={`sb-cal-day ${on ? 'on' : ''} ${dstr === today ? 'today' : ''}`}
                          onClick={() => toggleMultiDate(dstr)}>{i + 1}</button>
                      );
                    })}
                  </div>
                </div>
              );
            })()}
            <p className="sb-multi-info">
              선택: <b>{multiDates.size}</b>일 {multiDates.size > 0 && `(${[...multiDates].sort().join(', ')})`}
            </p>
            <div className="modal-actions">
              <button className="btn btn-ghost" onClick={() => setMultiOpen(false)} disabled={capturing}>취소</button>
              <button className="btn btn-primary" onClick={runMultiCapture} disabled={capturing || multiDates.size === 0}>
                {capturing ? '캡처 중…' : `캡처하기 (${multiDates.size}일)`}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 설비 & 레시피 통합 관리 모달 */}
      {mgrOpen && (
        <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) setMgrOpen(false); }}>
          <div className="modal-box sb-mgr">
            <div className="sb-mgr-head">
              <div className="sb-mgr-tabs">
                <button className={`sb-mgr-tab ${mgrTab === 'equip' ? 'on' : ''}`} onClick={() => setMgrTab('equip')}>설비 ({equipments.length})</button>
                <button className={`sb-mgr-tab ${mgrTab === 'recipe' ? 'on' : ''}`} onClick={() => setMgrTab('recipe')}>레시피 ({recipes.length})</button>
              </div>
              <button className="btn btn-ghost sb-mini" onClick={() => setMgrOpen(false)}>닫기</button>
            </div>

            {mgrTab === 'equip' ? (
              <>
                <div className="sb-add-sec">
                  <p className="sb-mgr-hint">설비명·공정·특이사항을 나눠 입력하면 <b>MDC02 (ZRO) (Hot Chemical)</b>처럼 표시됩니다(빈 항목은 생략). ▲▼로 순서 변경, 유휴 체크 시 알약 표시. 삭제해도 기존 배치는 보존됩니다.</p>
                  <div className="sb-add-row">
                    <div className="sb-add-fld sb-add-fld-grow">
                      <span className="sb-add-lbl">설비명</span>
                      <input className="input" placeholder="예: MDC11" value={newEquipName}
                        onChange={e => setNewEquipName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') addEquip(); }} />
                    </div>
                    <div className="sb-add-fld sb-add-fld-grow">
                      <span className="sb-add-lbl">공정</span>
                      <input className="input" placeholder="예: POLY" value={newEquipProcess}
                        onChange={e => setNewEquipProcess(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') addEquip(); }} />
                    </div>
                    <div className="sb-add-fld sb-add-fld-grow">
                      <span className="sb-add-lbl">특이사항</span>
                      <input className="input" placeholder="예: Hot Chemical" value={newEquipNote}
                        onChange={e => setNewEquipNote(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') addEquip(); }} />
                    </div>
                    <div className="sb-add-fld sb-add-fld-grp">
                      <span className="sb-add-lbl">그룹</span>
                      <select className="input sb-grp-sel" value={newEquipGroup} onChange={e => setNewEquipGroup(e.target.value)}>
                        <option>MDC</option><option>MSC</option><option>NDC</option>
                      </select>
                    </div>
                    <button className="btn btn-primary sb-add-btn" onClick={addEquip}>추가</button>
                  </div>
                </div>
                <div className="sb-eq-cols">
                  <span className="sb-eq-c-move" />
                  <span className="sb-eq-c-name">설비명</span>
                  <span className="sb-eq-c-proc">공정</span>
                  <span className="sb-eq-c-note">특이사항</span>
                  <span className="sb-eq-c-grp">그룹</span>
                  <span className="sb-eq-c-idle">유휴</span>
                  <span className="sb-eq-c-del" />
                </div>
                <div className="sb-mgr-list">
                  {equipments.map((eq, i) => (
                    <div key={eq.id} className={`sb-equip-mgr-row ${eq.isIdle ? 'idle' : ''}`}>
                      <div className="sb-eq-move">
                        <button onClick={() => moveEquip(i, -1)} disabled={i === 0}>▲</button>
                        <button onClick={() => moveEquip(i, 1)} disabled={i === equipments.length - 1}>▼</button>
                      </div>
                      <input className="input sb-eq-name" defaultValue={eq.name}
                        onBlur={e => { const v = e.target.value.trim(); if (v && v !== eq.name) saveEquip(eq, { name: v }); }} />
                      <input className="input sb-eq-proc" defaultValue={eq.process} placeholder="—"
                        onBlur={e => { const v = e.target.value.trim(); if (v !== eq.process) saveEquip(eq, { process: v }); }} />
                      <input className="input sb-eq-note" defaultValue={eq.note} placeholder="—"
                        onBlur={e => { const v = e.target.value.trim(); if (v !== eq.note) saveEquip(eq, { note: v }); }} />
                      <select className="input sb-grp-sel" value={eq.groupName} onChange={e => saveEquip(eq, { groupName: e.target.value })}>
                        <option>MDC</option><option>MSC</option><option>NDC</option>
                      </select>
                      <label className="sb-eq-idle" title="유휴 설비로 표시">
                        <input type="checkbox" checked={eq.isIdle} onChange={e => saveEquip(eq, { isIdle: e.target.checked })} />
                      </label>
                      <button className="btn sb-del-btn" onClick={() => delEquip(eq)}>삭제</button>
                    </div>
                  ))}
                </div>
              </>
            ) : (
              <>
                <div className="sb-add-sec">
                  <p className="sb-mgr-hint">S2·HF·DI(분)를 입력하고 추가. <b>핫케미컬</b> 체크 시 S2 온도(기본 60℃)를 입력합니다. '수정'으로 값 변경, ★ 즐겨찾기.</p>
                  <div className="sb-add-row">
                    <div className="sb-add-fld sb-add-fld-grow">
                      <span className="sb-add-lbl">S2 (분)</span>
                      <input className="input" type="number" placeholder="예: 30" value={newS2} min={0} onChange={e => setNewS2(e.target.value)}
                        onKeyDown={e => { if (e.key === 'Enter') addRecipe(); }} />
                    </div>
                    <div className="sb-add-fld sb-add-fld-grow">
                      <span className="sb-add-lbl">HF (분)</span>
                      <input className="input" type="number" placeholder="예: 30" value={newHf} min={0} onChange={e => setNewHf(e.target.value)}
                        onKeyDown={e => { if (e.key === 'Enter') addRecipe(); }} />
                    </div>
                    <div className="sb-add-fld sb-add-fld-grow">
                      <span className="sb-add-lbl">DI (분)</span>
                      <input className="input" type="number" placeholder="예: 100" value={newDi} min={0} onChange={e => setNewDi(e.target.value)}
                        onKeyDown={e => { if (e.key === 'Enter') addRecipe(); }} />
                    </div>
                    <label className="sb-hotchem sb-add-hot"><input type="checkbox" checked={newHot} onChange={e => setNewHot(e.target.checked)} />Hot Chemical</label>
                    {newHot && (
                      <div className="sb-add-fld sb-add-fld-grp">
                        <span className="sb-add-lbl">온도 (℃)</span>
                        <input className="input" type="number" value={newTemp} min={0} onChange={e => setNewTemp(+e.target.value)}
                          onKeyDown={e => { if (e.key === 'Enter') addRecipe(); }} />
                      </div>
                    )}
                    <button className="btn btn-primary sb-add-btn" onClick={addRecipe}>추가하기</button>
                  </div>
                </div>
                <div className="sb-mgr-list">
                  {recipes.map(r => (
                    <div key={r.id} className="sb-recipe-mgr-row">
                      <button className={`sb-star ${r.isFavorite ? 'on' : ''}`} onClick={() => toggleFav(r)}>{r.isFavorite ? '★' : '☆'}</button>
                      {editRec?.id === r.id ? (
                        <div className="sb-rec-edit">
                          <label>S2<input type="number" value={editRec.s2Minutes} onChange={e => setEditRec({ ...editRec, s2Minutes: +e.target.value })} /></label>
                          <label>HF<input type="number" value={editRec.hfMinutes} onChange={e => setEditRec({ ...editRec, hfMinutes: +e.target.value })} /></label>
                          <label>DI<input type="number" value={editRec.diMinutes} onChange={e => setEditRec({ ...editRec, diMinutes: +e.target.value })} /></label>
                          <label className="sb-hotchem"><input type="checkbox" checked={editRec.s2Temperature != null}
                            onChange={e => setEditRec({ ...editRec, s2Temperature: e.target.checked ? (editRec.s2Temperature ?? 60) : null })} />Hot Chemical</label>
                          {editRec.s2Temperature != null && (
                            <label>온도<input type="number" min={0} value={editRec.s2Temperature}
                              onChange={e => setEditRec({ ...editRec, s2Temperature: +e.target.value })} /><span className="sb-unit">℃</span></label>
                          )}
                          <button className="btn btn-primary sb-mini" onClick={saveRecipeEdit}>저장</button>
                          <button className="btn btn-ghost sb-mini" onClick={() => setEditRec(null)}>취소</button>
                        </div>
                      ) : (
                        <>
                          <span className="sb-recipe-mgr-name">{r.displayText}</span>
                          <button className="btn sb-mini" onClick={() => setEditRec(r)}>수정</button>
                          <button className="btn sb-del-btn" onClick={() => delRecipe(r)}>삭제</button>
                        </>
                      )}
                    </div>
                  ))}
                </div>
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

// 배치 블록 렌더 (S2→HF→DI 3구간, 06:00 넘어가면 좌측에 래핑 복제)
function BlockView({ b, row, cellW, rowH, boardW, zoom }: { b: Block; row: number; cellW: number; rowH: number; boardW: number; zoom: number }) {
  const total = b.s2 + b.hf + b.di;
  const x = Math.round((b.startMinute / MIN_PER_CELL) * cellW) + 1;
  const y = 3;
  const h = Math.max(2, rowH - 6);
  const seg = (w: number, c: string) => w > 0 ? <div style={{ width: (w / MIN_PER_CELL) * cellW, background: c }} className="sb-seg" /> : null;
  const inner = (
    <>
      {seg(b.s2, COL.s2)}{seg(b.hf, COL.hf)}{seg(b.di, COL.di)}
      <span className="sb-blk-label" style={{ fontSize: Math.max(9, 11 * zoom) }}>{blockLabel(b)}</span>
    </>
  );
  const wrap = b.startMinute + total > TOTAL_MIN;
  const topPx = row * rowH + y;
  return (
    <>
      <div className="sb-block" style={{ left: x, top: topPx, height: h }}>{inner}</div>
      {wrap && <div className="sb-block" style={{ left: x - boardW, top: topPx, height: h }}>{inner}</div>}
    </>
  );
}
