using System.Globalization;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Domain.BusinessRules;

// OPER 화면 IN INSP/FI INSP 검사값의 입력·판정 규칙 (2026-09-14 WPF ViewModel에서 승격).
//
// 예전엔 이 규칙이 WPF의 InspectionParameterRowViewModel 안에만 있어 웹(Blazor)이 재사용하지 못하고 복제해야
// 했다 - 앱/웹 규칙이 따로 놀 위험(드리프트)을 없애기 위해 순수 규칙으로 옮기고 양쪽이 이것만 참조한다.
// 동작은 승격 전과 완전히 같다(InspectionValueRulesTests로 고정).
//
//  입력 방식은 파라미터 데이터 유형(마스터)을 따른다(2026-08-27 피드백):
//   - Numeric(0) : 숫자 수기 입력 + SPEC 판정
//   - Boolean(1) : Y/N 드롭다운(기본 N)
//   - Choice (2) : OK/NG/CC 드롭다운(합부판정)
//  Numeric이고 다측정 대상이며 ValueCount가 2 이상이면 A/B/C/D… 포인트로 나눠 입력하고, 저장은 '|'로 이어
//  InputValue 하나에 담는다(2026-09-01 피드백 #8).
public static class InspectionValueRules
{
    public const string PointLabels = "ABCDE";
    public const char PointSeparator = '|';

    public static bool IsYesNo(ParameterType type) => type == ParameterType.Boolean;
    public static bool IsOkNgCc(ParameterType type) => type == ParameterType.Choice;
    public static bool IsNumericInput(ParameterType type) => type == ParameterType.Numeric;

    // 표면먼지(PARTICLE)는 SPEC 판정 방향이 반대다(MAX 초과가 OUT, 나머지는 MIN 이하가 OUT).
    public static bool IsParticle(string? code, string? description)
        => (code ?? string.Empty).Trim().Contains("PARTICLE", StringComparison.OrdinalIgnoreCase)
           || (description?.Contains("표면먼지") ?? false);

    // 다측정 대상: 표면먼지(PARTICLE)·두께(THK)·표면거칠기(ROUGH)만(2026-09-07 피드백). 코드 또는 설명으로 판별.
    // 무게(WEIGHT) 등 나머지 계측은 ValueCount가 2 이상이어도 단일 입력칸이다.
    public static bool IsMultiMeasurable(string? code, string? description)
    {
        var c = (code ?? string.Empty).Trim();
        var d = description ?? string.Empty;
        return c.Contains("PARTICLE", StringComparison.OrdinalIgnoreCase) || d.Contains("표면먼지")
            || c.Contains("THK", StringComparison.OrdinalIgnoreCase) || d.Contains("두께")
            || c.Contains("ROUGH", StringComparison.OrdinalIgnoreCase) || d.Contains("표면거칠기");
    }

    public static int NormalizeValueCount(int valueCount) => valueCount <= 0 ? 1 : valueCount;

    public static bool IsMultiPoint(ParameterType type, string? code, string? description, int valueCount)
        => IsMultiMeasurable(code, description) && IsNumericInput(type) && NormalizeValueCount(valueCount) > 1;

    // 저장값("A|B|C")을 포인트별로 분해한다. 포인트 수는 ValueCount(최대 라벨 수 5)만큼, 빈 값은 null.
    public static IReadOnlyList<string?> SplitPoints(string? storedValue, int valueCount)
    {
        var parts = (storedValue ?? string.Empty).Split(PointSeparator);
        var count = Math.Min(NormalizeValueCount(valueCount), PointLabels.Length);
        var result = new string?[count];
        for (var i = 0; i < count; i++)
        {
            var v = i < parts.Length ? parts[i] : null;
            result[i] = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
        return result;
    }

    // 포인트 값을 '|'로 잇는다. 포인트가 없거나 전부 비면 null(미측정).
    public static string? JoinPoints(IEnumerable<string?> values)
    {
        var list = values.ToList();
        if (list.Count == 0) { return null; }
        if (list.All(string.IsNullOrWhiteSpace)) { return null; }
        return string.Join(PointSeparator, list.Select(v => (v ?? string.Empty).Trim()));
    }

    // 외관(Y/N)은 저장값이 없으면 기본 N. 판정/단일 계측은 빈 칸(작업자가 직접 선택/기입).
    public static string? DefaultInputValue(ParameterType type, string? storedValue)
        => !string.IsNullOrWhiteSpace(storedValue) ? storedValue : (IsYesNo(type) ? "N" : null);

    // 계측값이 SPEC을 벗어났으면 사유 문자열, 정상이면 null. 값이 비면(미측정) 검사하지 않는다.
    // values: 단일이면 (null, 값) 하나, 다측정이면 (포인트 라벨, 값) 목록. 첫 위반을 돌려준다.
    public static string? GetSpecOutReason(
        ParameterType type, string code, string? description,
        decimal? minValue, decimal? maxValue,
        IEnumerable<(string? Label, string? Raw)> values)
    {
        if (!IsNumericInput(type)) { return null; }
        var isParticle = IsParticle(code, description);

        foreach (var (label, raw) in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) { continue; }
            if (!decimal.TryParse(raw.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val)) { continue; }
            var suffix = label is null ? string.Empty : $" [{label}]";
            if (isParticle)
            {
                if (maxValue is { } mx && val > mx) { return $"{code}{suffix} 결과 {val} > MAX {mx} (SPEC OUT)"; }
            }
            else
            {
                if (minValue is { } mn && val <= mn) { return $"{code}{suffix} 결과 {val} ≤ MIN {mn} (SPEC OUT)"; }
            }
        }
        return null;
    }
}
