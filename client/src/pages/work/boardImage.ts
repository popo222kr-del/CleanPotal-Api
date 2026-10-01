// Daily 업무 보고 — 스케줄 보드 그림(주간/야간). 스케줄 보드의 '화면 캡처' 와 같은 모양을 캔버스에 직접 그린다.
// 화면에서는 <img> 로, 메일 복사에서는 그 이미지(PNG)를 본문에 넣는다. (보드 화면을 열지 않아도 되게 서버 기록으로 그린다)
import type { DailyBoardEq } from '../../api/types';

const START_HOUR = 7;                      // 보드는 07:00 부터 24시간
const TOTAL_MIN = 24 * 60;
const COL = { s2: '#E08B86', hf: '#E3C069', di: '#7FB6D9' };   // 스케줄 보드와 같은 색
const FONT = "'Noto Sans KR', 'Apple SD Gothic Neo', 'Malgun Gothic', sans-serif";

export type BoardShift = 'day' | 'night';
export const SHIFT_RANGE: Record<BoardShift, [number, number, string]> = {
  day: [0, 720, '주간 07:00~19:00'],
  night: [720, TOTAL_MIN, '야간 19:00~07:00'],
};

function roundRect(g: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  const rr = Math.min(r, w / 2, h / 2);
  g.beginPath();
  g.moveTo(x + rr, y); g.arcTo(x + w, y, x + w, y + h, rr); g.arcTo(x + w, y + h, x, y + h, rr);
  g.arcTo(x, y + h, x, y, rr); g.arcTo(x, y, x + w, y, rr); g.closePath();
}

// 크기 — 줄은 촘촘하게(19px), 폭은 보고서 폭에 딱 맞게 그린다(10분 칸 폭을 폭에 맞춰 계산).
// 작게 그려 늘리거나 크게 그려 줄이면 글자 크기가 들쭉날쭉해져서, 늘 실제 표시 폭 그대로 그린다.
const EQ_W = 180, ROW = 19, HEAD = 24, TITLE = 32, PAD = 8;
/** 메일에 넣을 그림 폭(px) — 메일 본문 폭과 같다. */
export const BOARD_W = 960;

/** 그 교대 범위의 보드 그림을 PNG data URL 로. width = 표시할 폭(px). 설비가 없으면 빈 문자열. */
export function drawBoard(equipment: DailyBoardEq[], shift: BoardShift, title: string, scale = 2, width = BOARD_W): string {
  if (equipment.length === 0 || typeof document === 'undefined') return '';
  const [from, to] = SHIFT_RANGE[shift];
  const span = to - from;
  const CELL = Math.max(6, (Math.max(600, width) - PAD * 2 - EQ_W) / (span / 10));
  const boardW = (span / 10) * CELL;
  const W = PAD * 2 + EQ_W + boardW;
  const H = PAD + TITLE + HEAD + equipment.length * ROW + PAD;
  const canvas = document.createElement('canvas');
  canvas.width = W * scale; canvas.height = H * scale;
  const g = canvas.getContext('2d');
  if (!g) return '';
  g.scale(scale, scale);
  g.fillStyle = '#fff'; g.fillRect(0, 0, W, H);

  // 제목
  g.fillStyle = '#1F2937'; g.font = `800 14px ${FONT}`; g.textAlign = 'center'; g.textBaseline = 'middle';
  g.fillText(title, W / 2, PAD + TITLE / 2 - 3);

  const x0 = PAD + EQ_W, y0 = PAD + TITLE, yBody = y0 + HEAD;
  const xOf = (min: number) => x0 + ((min - from) / 10) * CELL;

  // 머리: 설비 / 시간 축
  g.fillStyle = '#F8FAFC'; g.fillRect(PAD, y0, EQ_W + boardW, HEAD);
  g.fillStyle = '#64748B'; g.font = `700 11px ${FONT}`; g.textAlign = 'left';
  g.fillText('설비', PAD + 8, y0 + HEAD / 2);
  // 시간 머리는 정시만(10분 눈금 숫자는 칸이 좁아 빼고 세로줄로만 둔다)
  for (let m = from; m < to; m += 60) {
    g.fillStyle = '#1F2937'; g.font = `700 11px ${FONT}`; g.textAlign = 'left';
    g.fillText(`${String((START_HOUR + m / 60) % 24).padStart(2, '0')}:00`, xOf(m) + 3, y0 + HEAD / 2);
  }

  // 줄 배경(MDC 파랑·NDC 빨강 기운, 유휴는 빗금) + 설비 이름
  equipment.forEach((e, i) => {
    const y = yBody + i * ROW;
    g.fillStyle = e.group === 'MDC' ? '#F3F8FC' : e.group === 'NDC' ? '#FBF3F3' : '#FFFFFF';
    g.fillRect(PAD, y, EQ_W + boardW, ROW);
    if (e.isIdle) {
      g.save(); g.beginPath(); g.rect(x0, y, boardW, ROW); g.clip();
      g.strokeStyle = 'rgba(100,116,139,0.18)'; g.lineWidth = 4;
      for (let k = -ROW; k < boardW; k += 11) { g.beginPath(); g.moveTo(x0 + k, y + ROW); g.lineTo(x0 + k + ROW, y); g.stroke(); }
      g.restore();
    }
    g.fillStyle = '#EEF2F6'; g.fillRect(PAD, y + ROW - 1, EQ_W + boardW, 1);
    g.fillStyle = '#1F2937'; g.font = `600 11px ${FONT}`; g.textAlign = 'left';
    let label = e.name;
    while (label.length > 1 && g.measureText(label).width > EQ_W - (e.isIdle ? 40 : 14)) label = label.slice(0, -1);
    if (label !== e.name) label = label.slice(0, -1) + '…';
    g.fillText(label, PAD + 8, y + ROW / 2);
    if (e.isIdle) {
      g.fillStyle = '#94A3B8'; g.font = `700 8.5px ${FONT}`; g.textAlign = 'right';
      g.fillText('유휴', x0 - 6, y + ROW / 2);
    }
  });

  // 시간 세로줄
  for (let m = from; m <= to; m += 10) {
    g.fillStyle = m % 60 === 0 ? '#D5DCE5' : '#F1F4F8';
    g.fillRect(xOf(m), y0 + (m % 60 === 0 ? 0 : HEAD), 1, m % 60 === 0 ? HEAD + equipment.length * ROW : equipment.length * ROW);
  }
  g.fillStyle = '#D5DCE5'; g.fillRect(x0, y0, 1, HEAD + equipment.length * ROW);

  // 블록(S2→HF→DI) — 범위 밖은 잘라 내고, 07:00 을 넘어간 블록은 보드처럼 앞쪽에 이어 그린다
  g.save(); g.beginPath(); g.rect(x0, yBody, boardW, equipment.length * ROW); g.clip();
  equipment.forEach((e, i) => {
    const y = yBody + i * ROW + 2, h = ROW - 4;
    for (const b of e.blocks) {
      for (const start of [b.startMinute, b.startMinute - TOTAL_MIN]) {
        const total = b.s2 + b.hf + b.di;
        if (start + total <= from || start >= to) continue;
        const bx = xOf(start), bw = (total / 10) * CELL;
        g.save();
        g.shadowColor = 'rgba(0,0,0,0.18)'; g.shadowBlur = 3; g.shadowOffsetY = 1;
        roundRect(g, bx, y, bw, h, 4); g.fillStyle = COL.di; g.fill();
        g.restore();
        g.save(); roundRect(g, bx, y, bw, h, 4); g.clip();
        let sx = bx;
        for (const [len, col] of [[b.s2, COL.s2], [b.hf, COL.hf], [b.di, COL.di]] as const) {
          const w = (len / 10) * CELL;
          if (w > 0) { g.fillStyle = col; g.fillRect(sx, y, w, h); sx += w; }
        }
        g.fillStyle = '#fff'; g.font = `700 10px ${FONT}`; g.textAlign = 'center';
        g.shadowColor = 'rgba(0,0,0,0.35)'; g.shadowBlur = 1; g.shadowOffsetY = 1;
        let text = b.recipe;
        while (text.length > 1 && g.measureText(text).width > bw - 6) text = text.slice(0, -1);
        if (text !== b.recipe) text = text.slice(0, -1) + '…';
        if (bw > 18) g.fillText(text, bx + bw / 2, y + h / 2 + 0.5);
        g.restore();
      }
    }
  });
  g.restore();

  // 바깥 테두리
  g.strokeStyle = '#E2E8F0'; g.lineWidth = 1;
  g.strokeRect(PAD + 0.5, y0 + 0.5, EQ_W + boardW - 1, HEAD + equipment.length * ROW - 1);
  return canvas.toDataURL('image/png');
}
