namespace ProductionManagement.Application.Screens;

// 웹 OPER 화면 목록 - 앱(MainViewModel.operItems) OPER 아코디언과 같은 순서/이름.
// 사이드바 메뉴와 LOT 스캔 이동(스캔한 LOT의 현재 공정에 화면이 있는지)이 이 목록 하나를 같이 쓴다.
public static class OperScreens
{
    public sealed record Item(int Code, string Name, string Icon);

    public static readonly IReadOnlyList<Item> All = new Item[]
    {
        new(2000, "입고", "inbox"),
        new(2100, "입고검사", "inspect"),
        new(3000, "세정", "droplet"),
        new(4000, "건조", "sun"),
        new(4100, "Laser&CO2", "zap"),
        new(5000, "Bake", "flame"),
        new(7000, "출고검사", "clipboard"),
        new(7100, "포장완료", "package"),
        new(8100, "고객출하", "truck"),
    };

    public static bool Has(int operCode) => All.Any(i => i.Code == operCode);
}
