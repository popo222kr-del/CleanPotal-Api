/**
 * 새로 연 창(about:blank)을 우리 화면이 그대로 그려질 수 있는 상태로 만든다.
 *
 * 새 창은 스타일이 하나도 없는 빈 문서다. 여기에 지금 문서의 스타일을 그대로 옮겨 넣어야
 * 표·버튼·색이 본 화면과 같아진다. <link> 는 주소만 베끼면 되고(경로는 절대주소로 바꾼다 —
 * about:blank 에는 기준 경로가 없다), 개발 중에 끼어드는 <style> 은 내용을 그대로 옮긴다.
 */
export function prepareMesPopup(popup: Window, title: string): HTMLDivElement {
  const doc = popup.document;

  doc.title = title;
  doc.documentElement.lang = document.documentElement.lang || 'ko';
  doc.head.innerHTML = '';

  for (const node of Array.from(document.querySelectorAll('link[rel="stylesheet"], style'))) {
    if (node instanceof HTMLLinkElement) {
      const link = doc.createElement('link');
      link.rel = 'stylesheet';
      link.href = node.href;             // 절대주소(브라우저가 이미 풀어 놓은 값)
      doc.head.appendChild(link);
    } else {
      const style = doc.createElement('style');
      style.textContent = node.textContent;
      doc.head.appendChild(style);
    }
  }

  // 화면들이 height:100% 를 쓰므로(mes-page) 위에서부터 높이를 이어 준다.
  doc.documentElement.style.height = '100%';
  doc.body.style.height = '100%';
  doc.body.style.margin = '0';

  const container = doc.createElement('div');
  container.className = 'mes-popup-root';
  container.style.height = '100%';
  container.style.display = 'flex';
  container.style.flexDirection = 'column';
  container.style.minHeight = '0';
  doc.body.appendChild(container);

  return container;
}

/** 새 창을 띄운다. 브라우저가 막으면 null. 반드시 클릭 처리 안에서 바로 불러야 막히지 않는다. */
export function openMesPopup(name: string, width: number, height: number): Window | null {
  // 지금 창 가운데쯤에 띄운다 — 화면 구석에 나타나면 못 찾는다.
  const left = Math.max(0, window.screenX + Math.round((window.outerWidth - width) / 2));
  const top = Math.max(0, window.screenY + Math.round((window.outerHeight - height) / 2));
  const features = `popup=yes,width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`;
  return window.open('', name, features);
}
