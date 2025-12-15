// Assets/Editor/DataProcessing/ExcelDataProcessor.cs
using UnityEngine;
using UnityEditor;
using System.IO;
using System;
using System.Collections; // IList 사용
using System.Collections.Generic;
using System.Reflection;
using System.Linq; // 리플렉션 사용

public class ExcelDataProcessor : AssetPostprocessor
{
    private const string ExcelDataPath = "Assets/Data/ExcelData/";
    private const string OutputAssetPath = "Assets/Data/GameData_SO/";
    private const string GameDataAddressableGroup = "GameData";
    private const string ExcelExtension = ".xlsx";
    private const int DataStartRow = 2; // 데이터 시작 행 (헤더 제외)
    private const int WorksheetIndex = 1; // 처리할 워크시트 인덱스 (1부터 시작)
    private const string ItemDatabaseAssetName = "ItemDatabase.asset"; // ItemDatabaseSO 파일 이름

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        bool requiresSave = false; // 최종 저장/새로고침 필요 여부 플래그

        // 1. 처리할 엑셀 파일 목록 준비 (임포트, 이동된 파일)
        var filesToProcess = importedAssets.Concat(movedAssets)
                                          .Where(ShouldProcessFile) // 유효한 엑셀 파일 필터링
                                          .Distinct()
                                          .ToList();

        // 2. 삭제된 엑셀 파일에 해당하는 에셋 경로 목록 준비
        List<string> assetsToDeletePaths = new List<string>();
        foreach (string deletedPath in deletedAssets)
        {
            if (deletedPath.StartsWith(ExcelDataPath) &&
                deletedPath.EndsWith(ExcelExtension, StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(deletedPath).StartsWith("~$"))
            {
                Debug.Log($"[Deletion] 감시 대상 엑셀 파일 삭제 감지: {deletedPath}");
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(deletedPath);
                // *** 중요: FindScriptableObjectTypeByName이 ItemDataSO 같은 타입을 찾아야 함 ***
                Type targetSOType = FindScriptableObjectTypeByName(fileNameWithoutExtension);

                if (targetSOType != null && typeof(ScriptableObject).IsAssignableFrom(targetSOType)) // SO 타입인지 확인
                {
                    if (Directory.Exists(OutputAssetPath))
                    {
                        // 삭제 로직은 동일하게 파일 이름 기반으로 수행 (SO 타입 이름 + ID)
                        string searchPattern = $"{targetSOType.Name}_*.asset";
                        try
                        {
                            string[] foundPaths = Directory.GetFiles(OutputAssetPath, searchPattern)
                                                        .Select(p => p.Replace("\\", "/"))
                                                        .ToArray();
                            if (foundPaths.Length > 0)
                            {
                                assetsToDeletePaths.AddRange(foundPaths);
                                Debug.Log($"[Deletion] {deletedPath} 관련 에셋 {foundPaths.Length}개 삭제 예정.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[Deletion] 관련 에셋 검색 중 오류 ({OutputAssetPath}, {searchPattern}): {ex.Message}");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"[Deletion] 삭제된 엑셀 파일({deletedPath})에 해당하는 SO 타입을 찾을 수 없거나 SO 타입이 아닙니다. 관련 에셋 자동 삭제를 건너<0xEB><0x9B><0x8D>니다.");
                }
            }
        }
        assetsToDeletePaths = assetsToDeletePaths.Distinct().ToList(); // 중복 제거

        // 3. 실제 작업 수행 (삭제 또는 생성/업데이트할 내용이 있을 경우)
        if (assetsToDeletePaths.Any() || filesToProcess.Any())
        {
            AssetDatabase.StartAssetEditing(); // <<< 모든 에셋 변경 작업을 하나의 블록으로 묶음
            try
            {
                // 3.1. 에셋 삭제 수행
                if (assetsToDeletePaths.Any())
                {
                    foreach (string assetPath in assetsToDeletePaths)
                    {
                        if (AssetDatabase.DeleteAsset(assetPath))
                        {
                            Debug.Log($"[Deletion] 관련 에셋 삭제 완료: {assetPath}");
                            requiresSave = true; // 삭제 작업도 저장 필요
                        }
                        else
                        {
                            Debug.LogWarning($"[Deletion] 에셋 삭제 실패 시도: {assetPath}");
                        }
                    }
                }

                // 3.2. 에셋 생성/업데이트 수행
                if (filesToProcess.Any())
                {
                    foreach (string excelFilePath in filesToProcess)
                    {
                        if (ProcessExcelFile(excelFilePath)) // ProcessExcelFile이 변경사항이 있었으면 true 반환
                        {
                            requiresSave = true; // 생성/업데이트 작업으로 저장 필요
                        }
                    }
                }

                bool itemDataChanged = filesToProcess.Any(f => Path.GetFileNameWithoutExtension(f).Equals("ItemData", StringComparison.OrdinalIgnoreCase));
                // || 삭제된 에셋 중에 ItemDataSO가 있을 경우 등 추가 조건 가능

                if (itemDataChanged)
                {
                    Debug.Log("[Update DB] ItemData 변경 감지. ItemDatabaseSO 업데이트를 시도합니다.");
                    UpdateItemDatabaseSO(); // 이 함수는 이제 조건부로만 호출됩니다.
                    requiresSave = true;

                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Asset 처리 중 예외 발생: {ex}");
            }
            finally
            {
                AssetDatabase.StopAssetEditing(); // <<< 모든 에셋 변경 작업 완료
            }
        }

        // 4. 최종 저장 및 새로고침 (모든 작업 완료 후, 변경사항이 있었을 경우에만)
        if (requiresSave)
        {
            AssetDatabase.SaveAssets(); // <<< 디스크에 변경사항 저장
            AssetDatabase.Refresh();    // <<< 에셋 데이터베이스 새로고침
            Debug.Log("=== 엑셀 데이터 처리 완료 ===");
        }
        else if (filesToProcess.Any())
        {
            Debug.Log("엑셀 파일 변경 감지되었으나, 실제 데이터 변경은 없었습니다.");
        }
    }

    // 파일 경로가 처리 대상인지 확인하는 헬퍼 함수
    static bool ShouldProcessFile(string path)
    {
        return !string.IsNullOrEmpty(path) &&
               path.StartsWith(ExcelDataPath, StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(ExcelExtension, StringComparison.OrdinalIgnoreCase) &&
               !Path.GetFileName(path).StartsWith("~$");
    }

    // 단일 엑셀 파일을 처리하는 함수
    static bool ProcessExcelFile(string excelFilePath)
    {
        Debug.Log($"[Process] 엑셀 파일 처리 시작: {excelFilePath}");
        bool changedInFile = false; // 이 파일 처리로 인해 변경사항이 발생했는지 여부

        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(excelFilePath);
        // *** 중요: 파일 이름(예: "ItemData")으로 SO 타입(예: "ItemDataSO")을 찾아야 함 ***
        Type targetSOType = FindScriptableObjectTypeByName(fileNameWithoutExtension);

        if (targetSOType == null)
        {
            Debug.LogError($"[Process] 대상 ScriptableObject 타입을 찾지 못했습니다: '{fileNameWithoutExtension}'. 파일: {excelFilePath}");
            return false;
        }
        if (!typeof(ScriptableObject).IsAssignableFrom(targetSOType))
        {
            Debug.LogError($"[Process] 찾은 타입 '{targetSOType.Name}'은 ScriptableObject가 아닙니다. 파일: {excelFilePath}");
            return false;
        }

        // *** itemID 필드 찾기 (ExcelHeaderAttribute.ItemIDHeader 사용, 타입은 int여야 함) ***
        FieldInfo itemIdField = FindFieldWithAttribute(targetSOType, ExcelHeaderAttribute.ItemIDHeader);
        if (itemIdField == null)
        {
            Debug.LogError($"[Process] SO 타입 '{targetSOType.Name}'에 [ExcelHeader(\"{ExcelHeaderAttribute.ItemIDHeader}\")] 속성을 가진 필드가 없습니다. 파일: {excelFilePath}");
            return false;
        }



        try
        {
            // 1. 데이터 로드 (ExcelDataReaderUtility 사용)
            // !!! 중요 !!!: LoadData<T>는 'new()' 제약조건이 있어 SO 타입을 직접 T로 사용할 수 없습니다.
            // 이 코드는 LoadData가 ScriptableObject 인스턴스를 반환하도록 수정되었거나,
            // T가 SO 필드를 가진 임시 클래스라고 가정합니다.
            // 만약 LoadData가 POCO 리스트를 반환한다면, 아래 로직에서 POCO -> SO 매핑이 필요합니다.
            MethodInfo loadDataMethod = typeof(ExcelDataReaderUtility).GetMethod("LoadData", BindingFlags.Public | BindingFlags.Static);
            if (loadDataMethod == null) throw new MissingMethodException("ExcelDataReaderUtility", "LoadData");

            // *** targetSOType을 제네릭 인자로 사용 (LoadData 수정 또는 POCO 사용 가정) ***
            MethodInfo genericLoadDataMethod = loadDataMethod.MakeGenericMethod(targetSOType);
            object loadedDataResult = genericLoadDataMethod.Invoke(null, new object[] { excelFilePath, WorksheetIndex, DataStartRow });

            if (loadedDataResult == null || !(loadedDataResult is IList loadedItems))
            {
                // LoadData 내부에서 이미 오류 로그가 찍혔을 수 있음
                Debug.LogError($"[Process] '{excelFilePath}'에서 데이터 로딩 실패 또는 결과가 리스트가 아님.");
                return false;
            }

            // 2. 대상 폴더 확인/생성
            if (!Directory.Exists(OutputAssetPath))
            {
                Directory.CreateDirectory(OutputAssetPath);
                // AssetDatabase.Refresh(); // 필요시
            }

            // 3. 에셋 생성/업데이트/삭제
            HashSet<string> currentItemIDs = new HashSet<string>(); // 현재 엑셀 파일에 있는 아이템 ID 집합 (int로 변경)

            // --- 데이터 처리 루프 ---
            foreach (object loadedItem in loadedItems) // loadedItem은 targetSOType 인스턴스라고 가정
            {
                if (loadedItem == null) continue;

                // LoadData가 SO 인스턴스를 반환한다고 가정하고 진행
                ScriptableObject dataInstance = loadedItem as ScriptableObject;
                if (dataInstance == null)
                {
                    Debug.LogWarning($"[Process] LoadData 결과가 ScriptableObject가 아님 ({loadedItem.GetType().Name}). 건너<0xEB><0x9B><0x8D>니다. 파일: {excelFilePath}");
                    continue;
                }
                // itemID 값 가져오기
                object itemIdValueObj = itemIdField.GetValue(dataInstance);
                string idAsString = itemIdValueObj?.ToString();
                
                if (string.IsNullOrWhiteSpace(idAsString))
                {
                    Debug.LogWarning($"[Process] 로드된 데이터에서 유효한 ID 값을 얻을 수 없음. 건너뜁니다.");
                    continue;
                }
                if (dataInstance is IPostProcessData postProcessable)
                {
                    postProcessable.PostProcess();
                }

                // 문자열 ID를 그대로 Set에 추가
                currentItemIDs.Add(idAsString);

                // 에셋 경로 및 이름 생성 (이제 ID는 항상 문자열)
                string assetFileName = $"{targetSOType.Name}_{idAsString}.asset";
                string assetPath = Path.Combine(OutputAssetPath, assetFileName).Replace("\\", "/");
                string expectedName = Path.GetFileNameWithoutExtension(assetPath);

                // --- 기존 에셋 로드 ---
                ScriptableObject existingAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

                // *** 중요: 저장/생성 전 이름 설정 ***
                dataInstance.name = expectedName;

                // --- 생성 또는 업데이트 ---
                if (existingAsset == null)
                {
                    // 새 에셋 생성 (LoadData가 이미 인스턴스를 생성했다고 가정)
                    AssetDatabase.CreateAsset(dataInstance, assetPath);
                    Debug.Log($"[Process] 새 에셋 생성: {assetPath}");
                    changedInFile = true;
                }
                else
                {
                    // 기존 에셋 업데이트
                    if (existingAsset.GetType() != targetSOType)
                    {
                        Debug.LogWarning($"[Process] 타입 불일치. 기존 에셋 삭제 후 재생성: {assetPath}");
                        AssetDatabase.DeleteAsset(assetPath);
                        AssetDatabase.CreateAsset(dataInstance, assetPath); // 새 타입으로 생성
                        changedInFile = true;
                    }
                    else
                    {
                        // 내용 복사 및 Dirty 마킹 (SO 인스턴스 간 복사)
                        EditorUtility.CopySerialized(dataInstance, existingAsset);
                        if (existingAsset.name != expectedName) // 이름 재확인/설정
                        {
                            existingAsset.name = expectedName;
                        }
                        EditorUtility.SetDirty(existingAsset);
                        // Debug.Log($"[Process] 기존 에셋 업데이트: {assetPath}"); // 로그 줄이기
                        changedInFile = true;
                    }
                }
#if ADDRESSABLES_PACKAGE_PRESENT
            // 주소는 에셋 경로와 동일하게 설정합니다. 이것이 가장 일반적인 규칙입니다.
            string address = assetPath; 
            AddressableManager.SetAssetAddress(assetPath, GameDataAddressableGroup, address);
#endif
            } // End foreach (loadedItem)

            // 4. 엑셀 파일에서 사라진 데이터에 해당하는 에셋 삭제
            string searchPattern = $"{targetSOType.Name}_*.asset";
            string[] existingAssetPathsInFolder = Directory.Exists(OutputAssetPath)
                ? Directory.GetFiles(OutputAssetPath, searchPattern).Select(p => p.Replace("\\", "/")).ToArray()
                : Array.Empty<string>();

            foreach (string existingPath in existingAssetPathsInFolder)
            {
                string fileName = Path.GetFileNameWithoutExtension(existingPath);
                int lastUnderscoreIndex = fileName.LastIndexOf('_');
                if (lastUnderscoreIndex != -1 && lastUnderscoreIndex < fileName.Length - 1)
                {
                    string idPart = fileName.Substring(lastUnderscoreIndex + 1);
                    // *** ID 부분을 int로 파싱 ***


                    if (!currentItemIDs.Contains(idPart)) // 현재 엑셀 파일에 ID가 없다면
                    {
                        // --- 삭제 전에도 Addressable 등록을 먼저 해제합니다. ---
#if ADDRESSABLES_PACKAGE_PRESENT
                AddressableManager.RemoveAssetAddress(existingPath);
#endif
                        if (AssetDatabase.DeleteAsset(existingPath))
                        {
                            Debug.Log($"[Process] 엑셀에서 제거되어 에셋 삭제: {existingPath}");
                            changedInFile = true;
                        }
                        else
                        {
                            Debug.LogWarning($"[Process] 에셋 삭제 실패 시도: {existingPath}");
                        }
                    }


                }
                else
                {
                    Debug.LogWarning($"[Process] 에셋 파일 이름 형식 오류 (예상: TypeName_ItemID): {existingPath}");
                }
            } // End foreach (existingPath)
        }
        catch (TargetInvocationException tie) // LoadData<T> 호출 관련 예외
        {
            // LoadData 내부 오류일 가능성이 높음 (특히 new T() 제약조건 문제)
            Debug.LogError($"[Process] LoadData 호출 중 내부 예외 발생: {excelFilePath}. 내부 오류: {tie.InnerException?.ToString() ?? tie.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Process] 처리 중 예외 발생: {excelFilePath}. 오류: {ex.ToString()}");
            return false;
        }

        Debug.Log($"[Process] 엑셀 파일 처리 완료{(changedInFile ? " (변경됨)" : " (변경 없음)")}: {excelFilePath}");
        return changedInFile; // 변경 여부 반환
    }


    // ItemDatabaseSO를 찾아 업데이트하는 함수
    private static void UpdateItemDatabaseSO()
    {
        string databasePath = Path.Combine(OutputAssetPath, ItemDatabaseAssetName).Replace("\\", "/");
        ItemDatabaseSO databaseSO = AssetDatabase.LoadAssetAtPath<ItemDatabaseSO>(databasePath);

        // 데이터베이스 SO가 없으면 새로 생성
        if (databaseSO == null)
        {
            Debug.Log($"[Update DB] ItemDatabaseSO '{ItemDatabaseAssetName}' 없음. 새로 생성 시도: {databasePath}");
            databaseSO = ScriptableObject.CreateInstance<ItemDatabaseSO>();
            try
            {
                // 폴더 생성은 ProcessExcelFile 단계에서 이미 처리되었을 가능성이 높음
                if (!Directory.Exists(OutputAssetPath)) Directory.CreateDirectory(OutputAssetPath);
                AssetDatabase.CreateAsset(databaseSO, databasePath);
                databaseSO = AssetDatabase.LoadAssetAtPath<ItemDatabaseSO>(databasePath); // 생성 후 다시 로드
                if (databaseSO == null) throw new InvalidOperationException("CreateAsset 후 LoadAssetAtPath 실패");
                // EditorUtility.SetDirty(databaseSO); // UpdateItemList가 호출될 것이므로 여기서 불필요
                Debug.Log($"[Update DB] ItemDatabaseSO 생성 완료: {databasePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Update DB] ItemDatabaseSO 생성 실패! 경로: {databasePath}, 오류: {ex.Message}");
                return; // 생성 실패 시 진행 불가
            }
        }

        // OutputAssetPath에서 모든 ItemDataSO 에셋 찾기 (ItemDatabaseSO 제외)
        List<ItemDataSO> allItemDataAssets = new List<ItemDataSO>();
        // *** 검색 타입을 ItemDataSO로 명시 ***
        string[] guids = AssetDatabase.FindAssets($"t:{nameof(ItemDataSO)}", new[] { OutputAssetPath });

        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            // 자기 자신(ItemDatabase)은 제외 (경로 비교)
            if (assetPath.Equals(databasePath, StringComparison.OrdinalIgnoreCase)) continue;

            ItemDataSO itemData = AssetDatabase.LoadAssetAtPath<ItemDataSO>(assetPath);
            if (itemData != null)
            {
                // *** itemID가 int인지 확인 (안전장치) ***
                if (itemData.itemID != default(int)) // 또는 다른 유효성 검사
                {
                    allItemDataAssets.Add(itemData);
                }
                else
                {
                    Debug.LogWarning($"[Update DB] 유효하지 않은 ItemID를 가진 ItemDataSO 발견: {assetPath}");
                }
            }
        }

        // 찾은 리스트를 ID 순으로 정렬 (int 기준)
        allItemDataAssets = allItemDataAssets.OrderBy(item => item.itemID).ToList();

        // 데이터베이스 SO 업데이트 (ItemDatabaseSO의 UpdateItemList 호출)
        // UpdateItemList가 내부적으로 SetDirty를 호출함
        databaseSO.UpdateItemList(allItemDataAssets); // bool 반환 값은 여기선 사용 안 함

        // Debug.Log($"[Update DB] ItemDatabaseSO 업데이트 완료. {allItemDataAssets.Count}개 아이템 포함.");
        // UpdateItemList 내부 로그에 위임
    }


    // ScriptableObject 타입을 파일 기본 이름으로 찾는 함수 (예: "ItemData" -> ItemDataSO)
    private static Type FindScriptableObjectTypeByName(string baseName)
    {
        // 파일 이름 + "SO" 규칙 사용 (필요시 다른 규칙 추가)
        string targetTypeName = baseName + "SO";
        Debug.Log($"[FindSOType] SO 타입 검색 시도: 기본 이름 '{baseName}', 예상 타입명 '{targetTypeName}'");

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                // 네임스페이스 없이 타입 이름으로 검색 (대소문자 무시)
                var foundType = assembly.GetType(targetTypeName, false, true); // throwOnError=false, ignoreCase=true

                if (foundType != null && foundType.IsSubclassOf(typeof(ScriptableObject)) && !foundType.IsAbstract)
                {
                    Debug.Log($"[FindSOType] 타입 찾음: {foundType.FullName}");
                    return foundType;
                }

                // 특정 네임스페이스를 안다면 더 정확하게 검색 가능:
                // var qualifiedName = "YourNamespace." + targetTypeName;
                // foundType = assembly.GetType(qualifiedName, false, true);
                // if (foundType != null && ...) return foundType;

            }
            catch (ReflectionTypeLoadException ex) { Debug.LogWarning($"[FindSOType] 어셈블리 로드 오류 {assembly.FullName}: {ex.Message}"); }
            catch (Exception ex) { Debug.LogWarning($"[FindSOType] 어셈블리 확인 오류 {assembly.FullName}: {ex.Message}"); }
        }
        Debug.LogError($"[FindSOType] ScriptableObject 타입 '{targetTypeName}'을 찾지 못했습니다 (기본 이름: '{baseName}'). 클래스 이름과 파일 이름 규칙을 확인하세요.");
        return null;
    }

    // 지정된 타입에서 특정 Excel 헤더 이름을 가진 필드를 찾는 함수 (int 타입 ID 사용)
    private static FieldInfo FindFieldWithAttribute(Type type, string excelHeaderName)
    {
        if (type == null || string.IsNullOrEmpty(excelHeaderName)) return null;

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy); // 상속 필드 포함 가능성

        foreach (var field in fields)
        {
            var attribute = field.GetCustomAttribute<ExcelHeaderAttribute>(false); // 상속된 속성 제외
            if (attribute != null && attribute.HeaderName.Equals(excelHeaderName, StringComparison.OrdinalIgnoreCase))
            {
                // itemID 필드는 int 타입이어야 함 (여기서 추가 검사 가능하나, 호출부에서 이미 함)
                // if (excelHeaderName.Equals(ExcelHeaderAttribute.ItemIDHeader, StringComparison.OrdinalIgnoreCase) && field.FieldType != typeof(int))
                // {
                //     Debug.LogError($"[FindField] '{type.Name}'의 '{field.Name}' 필드는 [ExcelHeader(\"{ExcelHeaderAttribute.ItemIDHeader}\")] 속성을 가졌지만 타입이 'int'가 아닙니다.");
                //     return null; // 또는 예외 발생
                // }
                return field; // 찾음!
            }
        }
        return null; // 못 찾음
    }
}