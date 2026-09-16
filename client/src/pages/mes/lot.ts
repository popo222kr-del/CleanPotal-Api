// MES 화면들이 같이 쓰는 값·표시 규칙. 화면마다 따로 적으면 서로 갈라진다.

/** 서버 LotStatus(ProductionManagement.Domain.Enums.LotStatus)의 숫자 값. */
export const LOT_STATUS = {
  Waiting: 0, InProgress: 1, Hold: 2, Rework: 3, Completed: 4, Cancelled: 5, Void: 6,
} as const;

const LABELS: Record<number, string> = {
  0: '대기', 1: '진행중', 2: 'HOLD', 3: '재작업', 4: '완료', 5: '취소', 6: '무효',
};
const TONES: Record<number, string> = {
  0: 'wait', 1: 'run', 2: 'hold', 3: 'rework', 4: 'done', 5: 'off', 6: 'off',
};

export const statusLabel = (v: number) => LABELS[v] ?? String(v);
/** CSS 클래스 접미사 — .mes-badge.run 처럼 쓴다 */
export const statusTone = (v: number) => TONES[v] ?? 'off';

const pad = (n: number) => String(n).padStart(2, '0');

/** 'YYYY-MM-DD HH:mm' */
export function dateTime(iso: string | null | undefined) {
  if (!iso) return '';
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** 'YYYY-MM-DD' */
export function dateOnly(iso: string | null | undefined) {
  if (!iso) return '';
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** 지금까지 얼마나 지났나 — '3일 4시간' / '12시간' / '40분' */
export function elapsed(iso: string) {
  const ms = Date.now() - new Date(iso).getTime();
  if (!Number.isFinite(ms) || ms < 0) return '-';
  const min = Math.floor(ms / 60000);
  if (min < 60) return `${min}분`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}시간`;
  return `${Math.floor(hr / 24)}일 ${hr % 24}시간`;
}

/** 시간(소수)을 '12.5시간' 으로. 값이 없으면 '-' */
export const hours = (v: number | null | undefined) =>
  v === null || v === undefined ? '-' : `${Math.round(v * 10) / 10}시간`;

/** 대시보드 카드 클릭 시 서버에 넘기는 분류 (DashboardLotCategory 와 같은 이름) */
export type LotCategory =
  | 'TodayReceived' | 'InProgress' | 'Hold' | 'Rework'
  | 'ShippingWaiting' | 'LongWait' | 'WipStage' | 'TodayShipped';

/** 대시보드·조회 목록의 한 행 (서버 OperLotItemDto 중 화면이 쓰는 부분) */
export type OperLot = {
  lotId: number;
  lotNumber: string;
  productName: string;
  serialNumber: string;
  cleaningCode: string | null;
  customerName: string;
  quantity: number;
  currentStatus: number;
  receivedDate: string;
  stageArrivedAt: string;
  exportNumber: string | null;
  line: string | null;
  processLabel: string | null;
  pmEquipmentName: string | null;
  teamName: string | null;
  comment: string | null;
  currentProcessName: string | null;
};
