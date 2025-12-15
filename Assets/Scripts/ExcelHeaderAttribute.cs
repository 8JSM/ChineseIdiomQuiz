
using System;

/// <summary>
/// ScriptableObject 필드에 매핑될 엑셀 헤더 이름을 지정하는 어트리뷰트입니다.
/// 이 어트리뷰트가 없으면 필드 이름과 동일한 헤더를 찾습니다.
/// </summary>
[AttributeUsage(AttributeTargets.Field)] // 필드에만 적용 가능
public class ExcelHeaderAttribute : Attribute
{
    public string HeaderName { get; private set; }
    public const string ItemIDHeader = "itemID";

    public ExcelHeaderAttribute(string HeaderName)
    {
        if (string.IsNullOrWhiteSpace(HeaderName))
        {
            throw new ArgumentException("Excel column name cannot be null or whitespace.", nameof(HeaderName));
        }
        this.HeaderName = HeaderName;
    }
}