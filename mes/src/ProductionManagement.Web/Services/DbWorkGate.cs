namespace ProductionManagement.Web.Services;

// Blazor Server에서는 브라우저 연결(서킷) 하나가 DbContext 하나를 공유한다. 목록을 불러오는 중에 사용자가 다른 행을
// 누르는 식으로 이벤트가 겹치면 EF Core가 "A second operation was started on this context"로 실패한다.
// 화면의 DB 작업을 이 게이트로 감싸 한 번에 하나씩 차례로 실행되게 한다(요청을 버리지 않고 줄 세움).
//
// 재진입(2026-09-15 웹 QA #4): 게이트 안에서 실행 중인 같은 흐름이 다시 RunAsync를 부르면(예: 모달의 [저장]이 게이트 안에서
// 부모 화면의 OnSaved를 호출하고, 부모도 게이트를 쓰는 경우) 기다리지 않고 바로 실행한다. 흐름 구분은 AsyncLocal로 한다 -
// 사용자의 다른 클릭은 별도 흐름으로 들어오므로 여전히 줄을 선다.
public sealed class DbWorkGate : IDisposable
{
    private static readonly AsyncLocal<DbWorkGate?> Holder = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task RunAsync(Func<Task> work)
    {
        if (ReferenceEquals(Holder.Value, this))
        {
            await work();
            return;
        }

        await _semaphore.WaitAsync();
        try
        {
            Holder.Value = this;
            await work();
        }
        finally
        {
            Holder.Value = null;
            _semaphore.Release();
        }
    }

    public void Dispose() => _semaphore.Dispose();
}
