import { Component, type ErrorInfo, type ReactNode } from 'react';

/**
 * 화면 하나가 렌더링 중 죽어도 앱 전체가 하얗게 비지 않도록 막는다.
 *
 * 하얀 화면은 원인을 전혀 알 수 없어 신고도, 진단도 어렵다.
 * 대표적인 경우가 프런트만 새로 올리고 서버가 옛 버전일 때다 — 서버가 새 필드를
 * 내려주지 않아 화면이 없는 값을 읽다 죽는다. 그래서 안내에 그 경우를 함께 적는다.
 */
interface Props { children: ReactNode }
interface State { error: Error | null }

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // 개발자 도구 콘솔에 남겨 원인을 추적할 수 있게 한다
    console.error('화면 오류:', error, info.componentStack);
  }

  render() {
    if (!this.state.error) return this.props.children;
    return (
      <div className="eb-wrap">
        <div className="eb-box">
          <h3>화면을 표시하지 못했습니다</h3>
          <p>
            새로고침해도 같은 문제가 계속되면, <b>서버가 최신 버전으로 배포됐는지</b> 확인해 주세요.
            화면만 새 버전이고 서버 프로그램이 옛 버전이면 이 오류가 납니다.
          </p>
          <pre className="eb-msg">{this.state.error.message}</pre>
          <div className="eb-acts">
            <button className="btn btn-ghost" onClick={() => this.setState({ error: null })}>다시 시도</button>
            <button className="btn btn-primary" onClick={() => location.reload()}>새로고침</button>
          </div>
        </div>
      </div>
    );
  }
}
