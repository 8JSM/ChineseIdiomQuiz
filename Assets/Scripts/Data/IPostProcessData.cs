using UnityEditor; // AssetDatabase를 사용하기 위해 필요

/// <summary>
/// 엑셀에서 데이터를 로드한 후, 추가적인 처리(예: 리소스 연결)가
/// 필요한 ScriptableObject가 구현하는 인터페이스입니다.
/// </summary>
public interface IPostProcessData
{
    /// <summary>
    /// 데이터 로드 후 ExcelDataProcessor에 의해 호출됩니다.
    /// 이 메소드 안에서 필요한 리소스를 찾고 연결하는 로직을 구현합니다.
    /// </summary>
    void PostProcess();
}