// 한글 입력기가 켜진 채로 바코드 스캐너를 찍으면 영문 키가 한글로 바뀌어 들어온다("1SN261014004P" → "1누261014004ㅔ").
// 두벌식 자판 기준으로 눌린 영문 키로 되돌린다(서버 KoreanKeys 와 같은 규칙). 대소문자는 알 수 없어 대문자로.

const CHO = ['r', 'R', 's', 'e', 'E', 'f', 'a', 'q', 'Q', 't', 'T', 'd', 'w', 'W', 'c', 'z', 'x', 'v', 'g'];
const JUNG = ['k', 'o', 'i', 'O', 'j', 'p', 'u', 'P', 'h', 'hk', 'ho', 'hl', 'y', 'n', 'nj', 'np', 'nl', 'b', 'm', 'ml', 'l'];
const JONG = ['', 'r', 'R', 'rt', 's', 'sw', 'sg', 'e', 'f', 'fr', 'fa', 'fq', 'ft', 'fx', 'fv', 'fg', 'a', 'q', 'qt', 't', 'T', 'd', 'w', 'c', 'z', 'x', 'v', 'g'];
/** 호환 자모 ㄱ(U+3131) ~ ㅣ(U+3163) 순서 그대로의 키 */
const JAMO = ['r', 'R', 'rt', 's', 'sw', 'sg', 'e', 'E', 'f', 'fr', 'fa', 'fq', 'ft', 'fx', 'fv', 'fg', 'a', 'q', 'Q', 'qt', 't', 'T', 'd', 'w', 'W', 'c', 'z', 'x', 'v', 'g',
  'k', 'o', 'i', 'O', 'j', 'p', 'u', 'P', 'h', 'hk', 'ho', 'hl', 'y', 'n', 'nj', 'np', 'nl', 'b', 'm', 'ml', 'l'];

const isHangul = (c: number) => (c >= 0xac00 && c <= 0xd7a3) || (c >= 0x3131 && c <= 0x3163);

/** 한글이 섞여 있으면 눌린 영문 키(대문자)로, 없으면 그대로(앞뒤 공백만 다듬음) */
export function toKeys(input: string): string {
  const s = (input ?? '').trim();
  if (![...s].some(ch => isHangul(ch.charCodeAt(0)))) return s;
  let out = '';
  for (const ch of s) {
    const c = ch.charCodeAt(0);
    if (c >= 0xac00 && c <= 0xd7a3) {
      const i = c - 0xac00;
      out += CHO[Math.floor(i / (21 * 28))] + JUNG[Math.floor((i % (21 * 28)) / 28)] + JONG[i % 28];
    } else if (c >= 0x3131 && c <= 0x3163) out += JAMO[c - 0x3131];
    else out += ch;
  }
  return out.toUpperCase();
}
