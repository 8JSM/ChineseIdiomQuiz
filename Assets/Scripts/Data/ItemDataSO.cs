using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[CreateAssetMenu(fileName = "ItemData", menuName = "Game Data/Item Data", order = 1)]
public class ItemDataSO : ScriptableObject
#if UNITY_EDITOR 
    , IPostProcessData
#endif
{

    [Header("기본 정보")]
    [Tooltip("아이템의 고유 ID")]
    [ExcelHeader("itemID")]
    public int itemID;

    [Header("아이콘")]
    [ExcelHeader("IconName")]
    public string IconName;

    [Tooltip("실제 아이콘 스프라이트 (자동 할당됨)")]
    public Sprite Icon;

    [Tooltip("아이템의 이름")]
    [ExcelHeader("itemName")]
    public string itemName;

    [TextArea(3, 5)]
    [Tooltip("아이템의 설명")]
    [ExcelHeader("Description")]
    public string description;

    [Header("능력치")]
    [Range(0, 100)]
    [Tooltip("아이템 공격력")]
    [ExcelHeader("Power")]
    public int attackPower;

    [Tooltip("아이템 가격")]
    [ExcelHeader("price")]
    public float price;

    [Tooltip("사용 가능 여부")]
    [ExcelHeader("isUsable")]
    public bool isUsable;


    [ExcelHeader("IsStackable")]
    public bool IsStackable;

    [ExcelHeader("MaxStack")]
    public int MaxStack;
#if UNITY_EDITOR
    public void PostProcess()
    {
        // 기존에 ExcelDataProcessor에 있던 아이콘 로드 로직이 여기로 이동!
        if (!string.IsNullOrEmpty(IconName))
        {
            string[] searchInFolders = { "Assets/Resources/Sprites/Items" };
            string[] guids = AssetDatabase.FindAssets($"t:Sprite {IconName}", searchInFolders);
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                Icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
            else
            {
                Debug.LogWarning($"[ItemDataSO] 아이템 ID {itemID}: 아이콘 '{IconName}'을 찾을 수 없습니다.");
            }
        }
    }

#endif
}
