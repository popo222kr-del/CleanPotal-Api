using ProductionManagement.Application.DTOs;
using ProductionManagement.Domain.BusinessRules;
using ProductionManagement.Domain.Enums;

namespace ProductionManagement.Web.Models;

// OPER 화면 IN INSP/FI INSP 표 한 행 (웹판).
// 입력 유형 분기·다측정·SPEC OUT 판정 규칙은 앱(WPF)과 공유하는 Domain의 InspectionValueRules가 단일 원본이다
// (2026-09-14 승격 - 예전에 이 파일에 있던 규칙 복제본은 제거). 여기서는 Blazor 바인딩용 상태만 들고 있다.
public sealed class InspectionRowModel
{
    private readonly ParameterType _parameterType;

    public int ParameterDefinitionId { get; }
    public string Code { get; }
    public string Description { get; }
    public decimal? MinValue { get; }
    public decimal? MaxValue { get; }
    public string? ReferenceInputValue { get; }

    public bool IsYesNo { get; }
    public bool IsOkNgCc { get; }
    public bool IsNumericInput { get; }
    public bool IsMultiPoint { get; }
    public bool IsSingleNumeric => IsNumericInput && !IsMultiPoint;
    public int ValueCount { get; }

    // 다측정일 때 A/B/C/D… 포인트별 입력 칸.
    public List<InspectionPointModel> Points { get; } = new();

    public string? InputValue { get; set; }
    public string? Comment { get; set; }

    public InspectionRowModel(InspectionParameterRowDto dto)
    {
        ParameterDefinitionId = dto.ParameterDefinitionId;
        Code = dto.Code;
        Description = dto.Description;
        MinValue = dto.MinValue;
        MaxValue = dto.MaxValue;
        ReferenceInputValue = dto.ReferenceInputValue;
        _parameterType = dto.ParameterType;

        IsYesNo = InspectionValueRules.IsYesNo(dto.ParameterType);
        IsOkNgCc = InspectionValueRules.IsOkNgCc(dto.ParameterType);
        IsNumericInput = InspectionValueRules.IsNumericInput(dto.ParameterType);
        ValueCount = InspectionValueRules.NormalizeValueCount(dto.ValueCount);
        IsMultiPoint = InspectionValueRules.IsMultiPoint(dto.ParameterType, dto.Code, dto.Description, dto.ValueCount);

        if (IsMultiPoint)
        {
            var values = InspectionValueRules.SplitPoints(dto.InputValue, ValueCount);
            for (var i = 0; i < values.Count; i++)
            {
                Points.Add(new InspectionPointModel(InspectionValueRules.PointLabels[i].ToString(), values[i]));
            }
            RebuildFromPoints();
        }
        else
        {
            InputValue = InspectionValueRules.DefaultInputValue(dto.ParameterType, dto.InputValue);
        }

        Comment = dto.Comment;
    }

    // 다측정 포인트 값이 바뀔 때마다 InputValue를 다시 만든다('|' 결합).
    public void RebuildFromPoints() => InputValue = InspectionValueRules.JoinPoints(Points.Select(p => p.Value));

    public string? SpecOutReason => InspectionValueRules.GetSpecOutReason(
        _parameterType, Code, Description, MinValue, MaxValue,
        IsMultiPoint
            ? Points.Select(p => ((string?)p.Label, p.Value))
            : new (string?, string?)[] { (null, InputValue) });

    public InspectionParameterInputDto ToInput() => new(ParameterDefinitionId, InputValue, Comment);
}

public sealed class InspectionPointModel
{
    public string Label { get; }
    public string? Value { get; set; }

    public InspectionPointModel(string label, string? value)
    {
        Label = label;
        Value = value;
    }
}
