using System;
using UnityEngine;

namespace Animation.Inventory
{
    public class InventoryValidationPointer : MonoBehaviour
    {
        [SerializeField] private Material validMaterial;
        [SerializeField] private Material invalidMaterial;
        [SerializeField] private float height = 0.01f;
        private Renderer thisRenderer;
        private InventoryItem inventoryItem;

        private void Start()
        {
            thisRenderer = GetComponent<Renderer>();
        }

        private void Update()
        {
            if (inventoryItem == null)
            {
                return;
            }
            transform.position = inventoryItem.CurrentPosition;
            thisRenderer.material = inventoryItem.IsPlaceable ? validMaterial : invalidMaterial;
            transform.rotation = inventoryItem.CurrentRotation;
            switch (inventoryItem.ItemType)
            {
                case InventoryItemType.Wall:
                    var euler = transform.eulerAngles;
                    euler.x = 90f;
                    transform.eulerAngles = euler;
                    break;
            }
        }
        
        public void SetInventoryItem(InventoryItem inventoryItem)
        {
            this.inventoryItem = inventoryItem;
            Vector3 scale = inventoryItem.Bounds.size + Vector3.one * 10f;;
            scale.y = height;
            transform.localScale = scale;
        }

        public void SetActive(bool active)
        {
            gameObject.SetActive(active);
        }
    }
}