using System.Collections.Generic;
using System.Linq;
using Animation.InteractionHelpers;
using Animation.Misc.Grid;
using Animation.ScreenShot;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Animation.Inventory
{
    public class UIInventoryManager : MouseInputBehaviour
    {
        [SerializeField] private InventoryManager inventory;
        [SerializeField] private UIInventoryItem itemPrefab;
        [SerializeField] private ResponsiveGrid grid;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private ScreenShotGetter screenShotGetter;
        private InventoryItem currentDraggingItem;
        [SerializeField] private bool initializeOnAwake = true;
        private bool isInitialized = false;
        private List<UIInventoryItem> currentItems = new List<UIInventoryItem>();
        private List<Texture2D> currentItemIcons;
        [SerializeField]
        private string addToItemsName = "_Item";
        [SerializeField]
        private bool autoLookAtAssetsForIcon;
        private Vector2 scrollValue;

        private void Awake()
        {
            if (initializeOnAwake)
            {
                Initialize();
            }
            inventory.OnPrefabAdded += AddPrefab;
            inventory.OnPrefabRemoved += RemovePrefab;
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        }

        private void OnScrollValueChanged(Vector2 value)
        {
            if (currentDraggingItem != null)
            {
                CancelCurrentDrag();
            }
        }

        public void Initialize()
        {
            if (isInitialized == false)
            {
                grid.Clear();
                foreach (var prefab in inventory.ItemsPrefabs)
                {
                    AddPrefab(prefab);
                }
            }
        }
        protected override void Update()
        {
            base.Update();
            if (currentDraggingItem == null || EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            var targetTransform = currentDraggingItem.CalculateAssetPosition(CurrentPointerPosition);
            if (targetTransform != null)
            {
                currentDraggingItem.RotateItem(targetTransform.Value.Rotation,true);
                currentDraggingItem.MoveItem(targetTransform.Value.HitPoint,true);   
            }

            if (Input.GetMouseButtonDown(1) && currentDraggingItem != null)
            {
                CancelCurrentDrag();
            }
        }

        private void OnItemClick(PointerInputBehaviour input)
        {
            SelectItem(input);
        }

        protected override void OnClickAction()
        {
            if (currentDraggingItem == null || EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }
            PlaceItem(false);
        }

        private void OnItemDragStarted(PointerInputBehaviour input)
        {
            SelectItem(input);
        }

        private void SelectItem(PointerInputBehaviour input)
        {
            if (currentDraggingItem != null)
            {
                CancelCurrentDrag();
            }
            var item = input.GetComponent<UIInventoryItem>();
            var targetTransform = inventory.CalculateAssetPosition(item.RelatedItem.ItemType, input.CurrentPointerPosition);
            currentDraggingItem = item.RelatedItem.CloneItem(targetTransform?.HitPoint ?? null, targetTransform?.Rotation ?? null);
        }


        // private void OnItemDragging(PointerInputBehaviour input)
        // {
        //
        // }

        private void OnItemDragFinished(PointerInputBehaviour input)
        {
            if (currentDraggingItem == null)
            {
                return;
            }

            if (EventSystem.current.IsPointerOverGameObject())
            {
                CancelCurrentDrag();
                return;
            }
            PlaceItem(true);
        }

        private void PlaceItem(bool destroyIfFailed)
        {
            var isPlaced = currentDraggingItem.PlaceItem();
            if (isPlaced)
            {
                currentDraggingItem = null;
            }
            if(destroyIfFailed && !isPlaced)
            {
                currentDraggingItem.DestoryItem();
            }
        }

        public void CancelCurrentDrag()
        {
            currentDraggingItem.DestoryItem();
            currentDraggingItem = null;
        }

        private void AddPrefab(InventoryItem prefab)
        {
            var newItem = Instantiate(itemPrefab);
            newItem.Initialize(prefab,prefab.Title + addToItemsName,GetAssetIcons(prefab));
            grid.AddItem(newItem.gameObject);
            newItem.OnClick += OnItemClick;
            newItem.OnStartDragging += OnItemDragStarted; 
            //newItem.OnDragging += OnItemDragging;
            newItem.OnFinishDragging += OnItemDragFinished;
            currentItems.Add(newItem);
        }

        private Texture2D GetAssetIcons(InventoryItem item)
        {
            return screenShotGetter == null ? null : screenShotGetter.GetScreenShot(item.gameObject,Vector3.zero, Vector3.zero,autoLookAtAssetsForIcon);
        }

        private void RemovePrefab(InventoryItem prefab)
        {
            UIInventoryItem relatedUIItem = currentItems.First(f => f.RelatedItem == prefab);
            currentItems.Remove(relatedUIItem);
            grid.RemoveItem(relatedUIItem.gameObject);
        }
    }
}