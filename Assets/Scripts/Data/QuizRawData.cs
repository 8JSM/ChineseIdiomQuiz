using UnityEngine;

public class QuizRawData 
{
   [ExcelHeader("questionID")]
    public string questionID;

    [ExcelHeader("questionText")]
    public string question;

    [ExcelHeader("answerText")]
    public string answer;
}
