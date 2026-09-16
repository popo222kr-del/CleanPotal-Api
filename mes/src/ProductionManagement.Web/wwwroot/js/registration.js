// 전산등록 표 붙여넣기(앱 RegistrationView의 Ctrl+V와 같은 기능).
// 엑셀에서 여러 칸/여러 행을 복사해 표에 붙여넣으면 탭으로 구분된 글자가 들어온다 → 기본 붙여넣기를 막고
// 서버(Register.razor PasteRows)로 넘겨 행으로 나눠 담는다. 탭이 없는 한 칸짜리 값은 입력칸에 그대로 붙여넣는다.
// 클립보드 읽기 권한이 필요 없는 paste 이벤트라 사내 Wi-Fi(HTTP) 접속에서도 동작한다.
export function attachPaste(element, dotNet) {
    const handler = e => {
        const text = e.clipboardData ? e.clipboardData.getData('text/plain') : '';
        if (!text || text.indexOf('\t') < 0) {
            return;
        }

        e.preventDefault();
        dotNet.invokeMethodAsync('PasteRows', text);
    };
    element.addEventListener('paste', handler);
    element._pmPasteHandler = handler;
}

export function detachPaste(element) {
    if (element && element._pmPasteHandler) {
        element.removeEventListener('paste', element._pmPasteHandler);
        delete element._pmPasteHandler;
    }
}
