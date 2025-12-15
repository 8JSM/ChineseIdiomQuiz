using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;
using System.Reflection;
using ClosedXML.Excel;
using Codice.CM.Interfaces;


public interface IFromString{
    void FillFromString(string name);
}
public static class ExcelDataReaderUtility 
{
   public const string ItemHeader = "itemID";

   public static List<T>LoadData<T>(string filePath,int worksheetIndex = 1, int startRow = 2 , string idHeaderName = "itemID")
   {

        if(startRow < 2){
            Debug.LogError("startRow는 2 이상이어야 한다.");
            return new List<T>();
        }

        var dataList = new List<T>();

        FileInfo fileInfo = new FileInfo(filePath);

        if(!fileInfo.Exists){
            Debug.LogError($"엑셀 파일을 찾을 수 없습니다.{filePath}");
            return dataList;

        }
        try{
            using(var fs = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))

            using(var workBook = new XLWorkbook(fs)){
                if(workBook.Worksheets.Count < worksheetIndex){

                    Debug.LogError($"워크시트 인덱스{worksheetIndex}를 찾을 수 없습니다.({workBook.Worksheets.Count}개), 파일: {filePath}");

                    return dataList;
                }

                var worksheet = workBook.Worksheet(worksheetIndex);

                var range = worksheet.RangeUsed();

                if(range == null || range.RowCount() < startRow){
                    Debug.LogError($"워크시트 '{worksheet.Name}' 인덱스{worksheetIndex} 헤더 외 데이터 없습니다. 파일 {filePath}");

                    return dataList;
                }

                int headerRowIndex = startRow -1;

                var headerRow = worksheet.Row(headerRowIndex);

                var columnMappingFromFile = GetHeaderColumnMapping(headerRow);

                if(!columnMappingFromFile.Any())
                {
                    Debug.LogWarning($"워크시트 {worksheet.Name}의 해더 행의 {headerRowIndex}에서 유효한 헤더를 찾지 못했습니다. 파일: {filePath}");
                    return dataList;
                }

                var fieldMapByExcelHeader = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
                FieldInfo itemIDFieldInfo = null;

                string itemIDHeaderNameFromAttribute = null;

                var type = typeof(T);
                var allFields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

                foreach(var field in allFields){
                    var attribute = field.GetCustomAttribute<ExcelHeaderAttribute>(false);

                    if(attribute != null){
                        string headerNameAttr = attribute.HeaderName;
                        if(!fieldMapByExcelHeader.ContainsKey(headerNameAttr)){
                            fieldMapByExcelHeader.Add(headerNameAttr, field);

                            if(headerNameAttr.Equals(idHeaderName , StringComparison.OrdinalIgnoreCase)){
                                itemIDFieldInfo = field;
                                itemIDHeaderNameFromAttribute = headerNameAttr;
                            }
                        }
                        else{

                            Debug.LogError($"타입 {type.Name}에 동일한 엑셀 헤더 {headerNameAttr}를 가리키는 [ExcelColumn] 어트리뷰트가 여러 개 있습니다.필드1: '{fieldMapByExcelHeader[headerNameAttr].Name}', 필드2: '{field.Name}'. 파일: {filePath}");

                            return dataList;

                        }
                        
                    }
                }
                if(itemIDFieldInfo == null){
                    Debug.LogError($"타입 '{type.Name}'의 필드 중 [ExcelHeader(\"{idHeaderName}\")] (또는 표준 ID 이름) 어트리뷰트를 가진 필드를 찾을 수 없습니다. 에셋 이름 생성을 위해 필수입니다. 파일: {filePath}");
                    return dataList;
                }
                if(!columnMappingFromFile.ContainsKey(itemIDHeaderNameFromAttribute)){
                    Debug.LogError($"워크시트 '{worksheet.Name}'의 헤더에 필수 열 '{itemIDHeaderNameFromAttribute}'가 없습니다(타입 '{type.Name}'의 '{itemIDFieldInfo.Name}' 필드에 지정됨). 파일: {filePath}");
                    return dataList;
                }

                int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? (startRow - 1);

                for(int currentRow = startRow; currentRow <= lastRow; currentRow++){
                    var row = worksheet.Row(currentRow);
                    if(row.IsEmpty()) continue;

                    int itemIDColumnIndex = columnMappingFromFile[itemIDHeaderNameFromAttribute];
                    string itemIdStr = worksheet.Cell(currentRow, itemIDColumnIndex).GetString();
                    if(string.IsNullOrWhiteSpace(itemIdStr)){
                        Debug.LogError($"Row {currentRow}: '{itemIDHeaderNameFromAttribute}' 값이 비어있습니다 이 행을 건너뜁니다. 파일: {filePath}");
                        continue;
                    }

                    T instance;
                    Type typeT = typeof(T);
                    if(typeof(ScriptableObject).IsAssignableFrom(typeT)){

                        ScriptableObject createdObj = ScriptableObject.CreateInstance(typeT);
                        if (createdObj == null)
                        {
                            Debug.LogError($"Row {currentRow}: ScriptableObject.CreateInstance({typeT.Name}) failed. File: {filePath}");
                            continue;
                        }
                        instance = (T)(object)createdObj;

                    }
                    else
                    { // T가 일반 클래스인 경우 (기본 생성자가 필요함)
                        try{
                            instance = Activator.CreateInstance<T>();

                        }
                        catch(MissingMethodException){
                            Debug.LogError($"Row {currentRow}: 타입 '{typeT.Name}'에 기본 생성자(parameterless constructor)가 없어 인스턴스를 생성할 수 없습니다. 파일: {filePath}");
                            continue;
                        }
                        catch(Exception ex){
                            Debug.LogError($"Row {currentRow}: 타입 '{typeT.Name}'의 인스턴스 생성 중 오류 발생: {ex.Message}. 파일: {filePath}");
                            continue;
                        }

                    }
                    bool rowHasError = false;

                    foreach(var excelHeaderMapping in columnMappingFromFile){
                        string actualHeaderName = excelHeaderMapping.Key;
                        int columnIndex = excelHeaderMapping.Value;

                        if(fieldMapByExcelHeader.TryGetValue(actualHeaderName, out FieldInfo targetField)){
                            var cell = worksheet.Cell(currentRow, columnIndex);

                            object value = GetCellValue(cell, targetField.FieldType, filePath, actualHeaderName);

                            if(value != null || !targetField.FieldType.IsValueType || Nullable.GetUnderlyingType(targetField.FieldType) != null){

                                try{
                                    targetField.SetValue(instance, value);

                                }
                                catch(Exception SetEx){
                                     Debug.LogError($"Row {currentRow}, Column '{actualHeaderName}' ({columnIndex}): 필드 '{targetField.Name}'에 값 설정 실패. 타입: {targetField.FieldType}, 받은 값 타입: {value?.GetType().Name ?? "null"}, 파일: {filePath}. 오류: {SetEx.Message}");

                                    rowHasError = true;

                                }
                            }
                            else if(value == null && targetField.FieldType.IsValueType && Nullable.GetUnderlyingType(targetField.FieldType) == null){
                                rowHasError = true;
                            }
                        }
                        
                    }if(!rowHasError){
                        dataList.Add(instance);
                    }
                }
            }

        }
        catch(IOException ex){
            Debug.LogError($"Excel 파일 접근 중 IO 오류 발생: {filePath}. 파일이 다른 프로그램에서 열려있거나 권한 문제가 없는지 확인하세요. 오류: {ex.Message}");
        }
        catch (Exception ex)
        {
            // 에러 메시지를 보여주고 (오류 내용 전체 포함)
            Debug.LogError($"Excel 데이터 로딩 중 예외 발생: {filePath}. 오류: {ex.ToString()}");
        }
        return dataList;

   }


   private static Dictionary<string, int> GetHeaderColumnMapping(IXLRow headerRow){


    var mapping = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    foreach(var cell in headerRow.CellsUsed()){

        string headerText = cell.GetValue<string>()?.Trim();
        if(!String.IsNullOrEmpty(headerText)){

            if(!mapping.ContainsKey(headerText)){

                mapping.Add(headerText, cell.Address.ColumnNumber);
            }

            else{ // 헤더 행 안에서 중복된 열 이름을 발견했을 때때
                Debug.LogWarning($"헤더 행({headerRow.RowNumber()}에 중복된 헤더 열 이름{headerText}가 있다. 첫 번째 열만 사용됩니다.{mapping[headerText]})");
            }
        }
    }
    return mapping;
   }

   // 셀 값을 목표타입으로 변환한 후 결과를 돌려주는 함수수
   private static object GetCellValue(IXLCell cell, Type targetType, string filePathLogging, string headerNameForLogging)
   {

    try{
        if(cell.IsEmpty()) return GetDefaultValueForType(targetType);
        if(targetType == typeof(string)) return cell.GetValue<string>();
        if(targetType == typeof(int)) return cell.GetValue<int>();
        if(targetType == typeof(bool)) return ParseBoolValue(cell, filePathLogging, headerNameForLogging);
        if(targetType.IsEnum) return ParseEnumValue(cell, targetType, filePathLogging, headerNameForLogging);
        
        if (typeof(IFromString).IsAssignableFrom(targetType))
            {
                string strValue = cell.GetValue<string>();
                if (string.IsNullOrWhiteSpace(strValue))
                {
                    return GetDefaultValueForType(targetType);
                }
                try
                {
                    var obj = Activator.CreateInstance(targetType);
                    (obj as IFromString).FillFromString(strValue);
                    return obj;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Cell: {cell.Address} Header: {headerNameForLogging} file: {filePathLogging} 타입 {targetType.Name}의 IFromString.FillFromString 실행 중 오류:{ex.Message}. 원본 값: '{strValue}'. 기본값을 반환합니다.");
                    return GetDefaultValueForType(targetType);
                }
            }

        object rawValue = cell.GetValue<object>();

        if(rawValue == null || rawValue is DBNull) return GetDefaultValueForType(targetType);
        Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if(underlyingType == typeof(float)) return Convert.ToSingle(rawValue, System.Globalization.CultureInfo.InvariantCulture);
        if(underlyingType == typeof(double)) return Convert.ToDouble(rawValue, System.Globalization.CultureInfo.InvariantCulture);
        if(underlyingType == typeof(decimal)) return Convert.ToDecimal(rawValue, System.Globalization.CultureInfo.InvariantCulture);

        return Convert.ChangeType(rawValue, underlyingType);
    }
    catch(FormatException fe){

        return LogConversionErrorAndGetDefault(cell, targetType, fe, "타입 실패 (FormatException)", filePathLogging, headerNameForLogging);

    }
    catch(InvalidCastException ice){
        return LogConversionErrorAndGetDefault(cell, targetType, ice, "타입 변환 실패 (InvalidCastException)", filePathLogging, headerNameForLogging);
    }
    catch(OverflowException oe){
        return LogConversionErrorAndGetDefault(cell, targetType, oe, "타입 변환 실패 (OverflowException)", filePathLogging, headerNameForLogging);
    }
    catch (Exception ex) {
        return LogConversionErrorAndGetDefault(cell, targetType, ex, "기타 오류", filePathLogging, headerNameForLogging);
        }


   }

   private static object GetDefaultValueForType(Type targetType)
   {

    // nullable타입이 아니고 값 타입이면 기본값 반환 값타입이 아니거나 값타입이 아닌 nuallable타입이면 null 반환환
    return targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null ? Activator.CreateInstance(targetType) : null; 
   }

   private static object ParseBoolValue(IXLCell cell, string filePathLogging, string headerNameForLogging)
   {

    try{
        return cell.GetValue<bool>();
    }
    catch{
        string str = cell.GetValue<string>().Trim().ToUpperInvariant();

        if(str == "TRUE" || str == "1" || str == "Y" ){
            return true;
        }
        if(str == "FASLE" || str == "0" || str == "N"){
            return false;
        }
        return LogBooleanConverionErrorAndGetDefault(cell, str, filePathLogging, headerNameForLogging);
    }

   }

   private static object ParseEnumValue(IXLCell cell, Type targetype, string filePathLogging, string headerNameForLogging)
   {
    string enumStr = cell.GetValue<string>();
    if(String.IsNullOrWhiteSpace(enumStr))
    {
        return Activator.CreateInstance(targetype);
    }
    try{
        return Enum.Parse(targetype, enumStr, true);
    }
    catch(ArgumentException ae){
        return LogEnumConversionErrorAndGetDefault(cell, targetype, enumStr, filePathLogging, headerNameForLogging, ae);
        }
    }
   
   private static object LogConversionErrorAndGetDefault(IXLCell cell, Type targetType, Exception ex, string errorMessage, string headerName, string filePath)
   {
        string valueStr = GetRawCellValueAsString(cell);

        Debug.LogError($"Cell: {cell.Address} header: {headerName} File: {filePath} Type: {targetType.Name} 오류: {errorMessage} 기본값을 반환합니다.");

        return GetDefaultValueForType(targetType);
   }

   private static bool LogBooleanConverionErrorAndGetDefault(IXLCell cell, string rawValueStr, string filePath, string headerName)
   {
        Debug.LogError($"Cell: {cell.Address} header: {headerName} file: {filePath} 값 {rawValueStr}을 boolean으로 변환할 수 없습니다.");

        return false;
   }

   private static object LogEnumConversionErrorAndGetDefault(IXLCell cell, Type targetType, string enumStr, string filePath, string headerName, ArgumentException ae)
   {
        Debug.LogError($"Cell: {cell.Address} header: {headerName} file: {filePath} 값 {enumStr}을(를) Enum타입 {targetType.Name}으로 변환할 수 없습니다.");

        return Activator.CreateInstance(targetType);
   }

   private static string GetRawCellValueAsString(IXLCell cell)
   {
    try{
        return cell.GetValue<string>();
        
    }
    catch{
        return "[값 읽기 오류]";
    }
   }


   
}
