using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using UnityEditor;
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Game Data/Item Database")]
public class ItemDatabaseSO : ScriptableObject
{
    // 에디터에서 ExcelDataProcessor가 자동으로 채워줄 리스트
    [SerializeField] // 인스펙터에서는 직접 수정하지 않도록 주의
    private List<ItemDataSO> allItems = new List<ItemDataSO>();

    // 런타임 성능을 위해 Dictionary 사용 (선택적이지만 권장)
    private Dictionary<int, ItemDataSO> itemsById;
    private bool isInitialized = false;

    private void OnEnable()
    {
        // 스크립트가 로드될 때마다 초기화 상태 리셋
        
        Debug.Log($"[ItemDatabaseSO] OnEnable called. Resetting initialization state.");
        itemsById = null; // 딕셔너리도 명시적으로 null 처리
        isInitialized = false;
    }

    
    private void InitializeDictionary()
    {
        if (isInitialized) return;
        Debug.Log($"[ItemDatabaseSO] Initializing Dictionary. allItems count: {allItems?.Count ?? 0}");

        itemsById = new Dictionary<int, ItemDataSO>(); // 대소문자 구분 없이 ID 사용
        foreach (var item in allItems)
        {
            if (item == null) continue;
            
            if (!itemsById.ContainsKey(item.itemID))
            {
                itemsById.Add(item.itemID, item);
            }
            else
            {
                Debug.LogWarning($"ItemDatabase에 중복된 itemID '{item.itemID}' 발견됨. 첫 번째 항목만 사용됩니다.");
            }
        }
        Debug.Log($"[ItemDatabaseSO] Dictionary Initialized. itemsById count: {itemsById?.Count ?? 0}");
        isInitialized = true;
        Debug.Log($"[ItemDatabaseSO] Initialized with {itemsById.Count} items.");
    }

    // 런타임에서 아이템을 ID로 가져오는 메소드
    public ItemDataSO GetItem(int id)
    {
        if (!isInitialized)
        {
            Debug.Log("[ItemDatabaseSO] GetItem detected not initialized. Calling InitializeDictionary...");
            InitializeDictionary();
        }

        if (itemsById != null && itemsById.TryGetValue(id, out ItemDataSO item))
        {
            return item;
        }
        // Debug.LogWarning($"ItemDatabase에서 ID '{id}'를 가진 아이템을 찾을 수 없습니다.");
        return null;
    }

    // 모든 아이템 리스트를 가져오는 메소드 (필요한 경우)
    public List<ItemDataSO> GetAllItems()
    {
        if (!isInitialized)
        {
            InitializeDictionary();
        }
        // 방어적 복사본 반환 또는 ReadOnlyCollection 반환 고려
        return new List<ItemDataSO>(allItems);
    }

#if UNITY_EDITOR
    // 에디터 전용: ExcelDataProcessor가 호출하여 리스트를 업데이트하는 메소드
    public void UpdateItemList(List<ItemDataSO> newItemList)
    {
        allItems = newItemList ?? new List<ItemDataSO>();
        isInitialized = false; // 다음에 접근 시 Dictionary 재생성하도록 플래그 설정
       EditorUtility.SetDirty(this); // 변경사항 저장 요청
        Debug.Log($"[ItemDatabaseSO] Updated with {allItems.Count} items by ExcelDataProcessor.");
    }
#endif
}