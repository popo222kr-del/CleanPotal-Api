import type { EqCheckItem, EqState } from '../../api/types';

// 체크시트 (설비) 화면 공통

export const CYCLES = ['일상', '주간', '월간'] as const;

/** 화면에 보이는 주기 이름 — 체크시트(현장)와 같게 '매일 / 주간 / 월간'. 저장 값(일상)은 그대로 둔다. */
export const CYCLE_LABEL: Record<string, string> = { 일상: '매일', 주간: '주간', 월간: '월간', 고장: '고장' };
export const cl = (c: string) => CYCLE_LABEL[c] ?? c;

/** 보기 — 첫 보기가 정상, '*' 는 조치함(NG 로 남기되 바로 조치 완료). */
export function optionsOf(i: Pick<EqCheckItem, 'options'>): { text: string; action: boolean }[] {
  return i.options.split('|').map(s => s.trim()).filter(Boolean)
    .map(s => (s.startsWith('*') ? { text: s.slice(1).trim(), action: true } : { text: s, action: false }));
}

export const fieldsOf = (i: Pick<EqCheckItem, 'fields'>) => i.fields.split('|').map(s => s.trim()).filter(Boolean);

const fmt = (v: number) => String(Math.round(v * 1000) / 1000);

/** 기준 범위 설명 — 입력 칸 옆에 보인다. */
export function rangeText(i: EqCheckItem): string {
  if (i.inputType === 'MULTI') return i.max != null ? `첫 칸과 ±${fmt(i.max)}${i.unit}` : '';
  if (i.min != null && i.max != null) return `${fmt(i.min)} ~ ${fmt(i.max)} ${i.unit}`.trim();
  if (i.max != null) return `${fmt(i.max)}${i.unit} 이하`;
  if (i.min != null) return `${fmt(i.min)}${i.unit} 이상`;
  return '';
}

export const STATE_LABEL: Record<EqState, string> = {
  done: '완료', partial: '진행 중', due: '오늘', late: '지연', todo: '예정', none: '—',
};
export const STATE_TONE: Record<EqState, string> = {
  done: 'ok', partial: '', due: 'warn', late: 'bad', todo: '', none: '',
};

export function md(ymd: string): string {
  const [, m, d] = ymd.split('-').map(Number);
  return `${m}/${d}`;
}

export function todayYmd(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
