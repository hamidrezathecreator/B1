using System.Collections.Generic;
using UnityEngine;

namespace Animation.Inventory
{
    public class TestAddPrefab : MonoBehaviour
    {
        [SerializeField] private InventoryManager inventoryManager;
        [SerializeField] private InventoryItem prefabToAdd;

        [ContextMenu("AddPrefab")]
        private void AddPrefab()
        {
            inventoryManager.AddPrefab(prefabToAdd);
        }
        
        [ContextMenu("RemovePrefab")]
        private void RemovePrefab()
        {
            inventoryManager.RemovePrefab(prefabToAdd);
        }
    }
}