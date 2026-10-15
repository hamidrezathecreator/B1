using Animation.Inventory;
using UnityEngine;

public class RotationTest : MonoBehaviour
{
    [SerializeField]
    private InventoryManager manager;
    [SerializeField]
    private InventoryItem currentItem;
    void Start()
    {
        manager.OnItemAdded += OnAddItem;
    }

    private void OnAddItem(InventoryItem item)
    {
        currentItem = item;
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            currentItem.RotateLeft();
        }   
        if (Input.GetKeyDown(KeyCode.Q))
        {
            currentItem.RotateRight();
        }
    }
}
