// QR 체크시트 화면들이 같이 쓰는 작은 도움 함수.

const DOW = ['일', '월', '화', '수', '목', '금', '토'];

/** '2026-10-07' → '10/7(수)' */
export function dayLabel(ymd: string): string {
  const d = new Date(`${ymd}T00:00:00`);
  return `${d.getMonth() + 1}/${d.getDate()}(${DOW[d.getDay()]})`;
}

export function ymdOf(d: Date): string {
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

export function timeLabel(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getMonth() + 1}/${d.getDate()} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

export const PHOTO_LABEL: Record<string, string> = { before: '작업 전', after: '작업 후', ng: 'NG', photo: '사진' };

/** 사진 정책에 따라 받아야 하는 사진 칸. NG 칸은 결과가 NG 일 때만 보인다. */
export function photoSlots(policy: string, result: string): ('before' | 'after' | 'ng' | 'photo')[] {
  const slots: ('before' | 'after' | 'ng' | 'photo')[] = [];
  if (policy === '작업 전' || policy === '작업 전·후') slots.push('before');
  if (policy === '작업 후' || policy === '작업 전·후') slots.push('after');
  if (policy === '항상') slots.push('photo');
  if (result === 'NG') slots.push('ng');
  return slots;
}

/** 이 사진 칸이 제출에 꼭 필요한가 */
export function photoRequired(policy: string, slot: string, result: string): boolean {
  if (result === 'NA' || result === '') return false;
  if (slot === 'ng') return policy === 'NG 시';
  return true;
}

export const WEEKDAYS = ['월', '화', '수', '목', '금', '토', '일'];
export const TIMINGS = ['주·야 각 1회', '주간조만', '야간조만', '주 1회', '이벤트 발생 시'];
export const PHOTO_POLICIES = ['없음', 'NG 시', '작업 전', '작업 후', '작업 전·후', '항상'];
