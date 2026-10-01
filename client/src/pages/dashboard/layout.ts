import { api } from '../../api/client';

// 대시보드 카드 구성 — 사람마다 보고 싶은 카드가 달라서 켜고 끄고 순서를 바꿀 수 있게 한다.
// 설정은 계정(/api/me/prefs/dashboard)에 두어 PC·폰이 같게 본다.
//
// - 카드는 권한으로 먼저 걸러진다. 여기서 켜도 권한이 없는 카드는 나오지 않는다.
// - 저장하는 것은 '숨긴 카드'와 '순서'뿐 — 나중에 새 카드가 생기면 숨기지 않은 것이므로 누구에게나 뜬다.
// - 맨 위 이상 알림(출고일 지남·미조치 NG 등)은 카드가 아니라 끌 수 없다. 놓치면 안 되는 것이다.

export type CardGroup = 'top' | 'site';
export interface CardDef { key: string; label: string; group: CardGroup; hint: string }

export const CARDS: CardDef[] = [
  { key: 'notice', label: '공지 & 일정', group: 'top', hint: '공지·다가오는 팀 일정·교육 일정' },
  { key: 'today', label: '오늘의 근무 현황', group: 'top', hint: '부서·팀별 주간/야간/휴무/교육 인원' },
  { key: 'checklist', label: '체크시트 (현장)', group: 'site', hint: '지금 교대 구역 제출·미조치 NG' },
  { key: 'eqcheck', label: '체크시트 (설비)', group: 'site', hint: '매일 점검 완료·주간/월간·미조치 NG' },
  { key: 'handover', label: '기타세정 현황', group: 'site', hint: '진행·오늘/내일 출고·지연' },
  { key: 'weekly', label: '주간세정 현황', group: 'site', hint: '진행·오늘/내일 출고·지연' },
  { key: 'prodreq', label: '생산팀 요청사항', group: 'site', hint: '미확인·진행·마감 지남' },
  { key: 'dispatch', label: '오늘 배차', group: 'site', hint: '오늘 배차 건수·업체' },
  { key: 'broken', label: 'BROKEN', group: 'site', hint: '이번 달·올해 건수' },
  { key: 'board', label: '스케줄 보드', group: 'site', hint: '오늘 작업이 잡힌 설비·비가동' },
  { key: 'chemical', label: '약액 교체', group: 'site', hint: '오늘 약액 교체한 설비' },
  { key: 'waste', label: 'KOH·폐액', group: 'site', hint: '오늘 KOH 사용·폐액 증가(전날 대비)' },
  { key: 'bake', label: 'BAKE 진행 현황', group: 'site', hint: '오늘 가동 오븐·그을음·Q\'TZ' },
];

export interface DashLayout { hidden: string[]; order: string[] }
export const DEFAULT_LAYOUT: DashLayout = { hidden: [], order: [] };

const PREF_KEY = 'dashboard';

/** 저장된 값 → 설정. 모르는 카드 이름은 버린다(카드가 없어졌을 수 있다). */
export function parseLayout(raw: unknown): DashLayout {
  const known = new Set(CARDS.map(c => c.key));
  const o = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
  const list = (v: unknown) => (Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string' && known.has(x)) : []);
  return { hidden: [...new Set(list(o.hidden))], order: [...new Set(list(o.order))] };
}

export async function loadLayout(): Promise<DashLayout> {
  try {
    const prefs = await api.get<Record<string, unknown>>('/api/me/prefs');
    return parseLayout(prefs[PREF_KEY]);
  } catch {
    return DEFAULT_LAYOUT;   // 설정을 못 읽어도 대시보드는 기본 구성으로 보여야 한다
  }
}

/** 저장. 기본 구성과 같으면 설정을 지운다(나중에 기본이 바뀌면 따라가게). */
export function saveLayout(l: DashLayout): Promise<unknown> {
  const isDefault = l.hidden.length === 0 && l.order.length === 0;
  return api.put(`/api/me/prefs/${PREF_KEY}`, isDefault ? null : l);
}

/** 한 묶음 안의 카드 순서 — 저장된 순서에 있는 것 먼저, 나머지(새 카드 등)는 기본 순서대로 뒤에. */
export function orderOf(group: CardGroup, l: DashLayout): string[] {
  const keys = CARDS.filter(c => c.group === group).map(c => c.key);
  const pos = (k: string) => { const i = l.order.indexOf(k); return i < 0 ? 1000 + keys.indexOf(k) : i; };
  return [...keys].sort((a, b) => pos(a) - pos(b));
}

export const isShown = (key: string, l: DashLayout) => !l.hidden.includes(key);
