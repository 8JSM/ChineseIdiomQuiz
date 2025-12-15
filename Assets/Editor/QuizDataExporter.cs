using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Linq;

public class QuizDataExporter
{
    // 처리할 엑셀 파일 경로
    private const string ExcelDataPath = "Assets/Data/ExcelData/QuizData.xlsx";
    // 생성될 JSON 파일 경로 (프로젝트 루트)
    private const string OutputJsonPath = "questions.json";

    [MenuItem("Tools/Export Quiz Data to JSON")]
    public static void ExportQuizData()
    {
        Debug.Log("퀴즈 데이터 JSON 변환 시작...");

        var rawDataList = ExcelDataReaderUtility.LoadData<QuizRawData>(ExcelDataPath, idHeaderName: "questionID");

        if (rawDataList == null || !rawDataList.Any())
        {
            Debug.LogError("엑셀 파일에서 데이터를 읽어오지 못했거나 데이터가 없습니다. 중단합니다.");
            return;
        }

        // 2. 서버가 사용할 JSON 형식으로 데이터 변환
        QuestionJsonList questionJsonList = new QuestionJsonList();

        foreach (var rawData in rawDataList)
        {
            // 데이터 유효성 검사
            if (string.IsNullOrWhiteSpace(rawData.question) || string.IsNullOrWhiteSpace(rawData.answer))
            {
                Debug.LogWarning($"ID '{rawData.questionID}' 데이터에 question 또는 answer가 비어있어 건너뜁니다.");
                continue;
            }

            questionJsonList.questions.Add(new QuestionJsonData
            {
                question = rawData.question,
                answer = rawData.answer
            });
        }

        // 3. JSON 문자열로 직렬화
        string json = JsonUtility.ToJson(questionJsonList, true); // true: 예쁘게 포맷팅

        // 4. 파일로 저장 (프로젝트 루트 경로 계산)
        string savePath = Path.Combine(Application.dataPath, "..", OutputJsonPath);

        try
        {
            // C++ 서버가 한글을 제대로 읽으려면 UTF-8 형식으로 저장해야 함
            File.WriteAllText(savePath, json, Encoding.UTF8);

            Debug.Log($"총 {questionJsonList.questions.Count}개의 문제를 '{savePath}' 경로에 성공적으로 저장했습니다.");

            // 편의 기능: 저장된 파일 위치 열기
            EditorUtility.RevealInFinder(savePath);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"JSON 파일 저장 중 오류 발생: {ex.Message}");
        }
    }
}
