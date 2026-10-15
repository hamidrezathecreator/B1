using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Animation.Inventory
{
    public class InventoryManager : MonoBehaviour
    {
        [SerializeField][HideInInspector] private List<InventoryItem> initialPrefabs;
        [SerializeField] private InventoryConfig config;
        [SerializeField] private InventoryValidationPointer validationPointer;
        [SerializeField] private bool initializeOnAwake = true;

        public bool InitializeOnAwake => initializeOnAwake;
        public List<InventoryItem> ItemsPrefabs { get; private set; } = new List<InventoryItem>();
        public IEnumerable<InventoryItem> InitialPrefabs => initialPrefabs;
        private List<InventoryItem> currentItems = new List<InventoryItem>();
        public event InventoryItemDelegate OnItemRemoved;
        public event InventoryItemDelegate OnItemAdded;
        public event InventoryItemDelegate OnUIItemCloned;
        public event InventoryItemDelegate OnItemChanged;
        public event InventoryItemDelegate OnItemRepositioned;
        public event InventoryItemDelegate OnItemPositionReverted;
        public event InventoryItemDelegate OnPrefabAdded;
        public event InventoryItemDelegate OnPrefabRemoved;
        public delegate void InventoryItemDelegate(InventoryItem item);

        private void Awake()
        {
            if (initializeOnAwake)
            {
                Initialize();
            }
        }
        
        private void OnDestroy()
        {
            foreach (var prefab in ItemsPrefabs)
            {
                prefab.OnCloned -= OnNewItemCloned;
            }
        }

        private void Initialize()
        {
            SetPrefabs(initialPrefabs);
            if (validationPointer != null)
            {
                validationPointer.SetActive(false);   
            }
        }

        public void SetPrefabs(IEnumerable<InventoryItem> prefabs)
        {
            ClearPrefabs();
            foreach (var prefab in prefabs)
            {
                AddPrefab(prefab);
            }
        }

        public void ClearPrefabs()
        {
            if (ItemsPrefabs == null)
            {
                ItemsPrefabs = new List<InventoryItem>();
            }
            while(ItemsPrefabs.Any())
            {
                RemovePrefab(ItemsPrefabs[0]);
            }
        }
        
        public void AddPrefab(InventoryItem newPrefab)
        {
            ItemsPrefabs.Add(newPrefab);
            newPrefab.OnCloned += OnNewItemCloned;
            OnPrefabAdded?.Invoke(newPrefab);
        }

        public void RemovePrefab(InventoryItem prefabToRemove)
        {
            if (ItemsPrefabs.Contains(prefabToRemove))
            {
                ItemsPrefabs.Remove(prefabToRemove);
                prefabToRemove.OnCloned -= OnNewItemCloned;
                OnPrefabRemoved?.Invoke(prefabToRemove);
            }
        }

        // public InventoryItem CloneFromPrefab(int prefabIndex)
        // {
        //     if (prefabIndex >= itemsPrefabs.Count) 
        //     {
        //         Debug.LogError("The selected prefab is out of index");
        //         return null;
        //     }
        //     var newItem = itemsPrefabs[prefabIndex].CloneItem();
        //     return newItem;
        // }

        private void OnNewItemCloned(InventoryItem newItem)
        {
            newItem.OnCloned += OnNewItemCloned;
            newItem.OnChanged += OnEachItemChanged;
            newItem.OnDestroying += OnEachItemDestroying;
            newItem.OnPlaced += OnEachItemPlaced;
            newItem.OnRepositioned += OnEachItemRepositioned;
            newItem.OnPositionReverted += OnEachItemPositionReverted;
            newItem.CheckPlacabilityFunc = CheckPlacability;
            newItem.CalculatePositionFunc = CalculateAssetPosition;
            newItem.ValidationPointer = validationPointer;
            OnUIItemCloned?.Invoke(newItem);
        }
        
        private void OnEachItemPlaced(InventoryItem item, bool ignoreNotify)
        {
            currentItems.Add(item);
            if (ignoreNotify == false)
            {
                OnItemAdded?.Invoke(item);
            }
        }
        
        private void OnEachItemRepositioned(InventoryItem item, bool ignoreNotify = false)
        {
            if (ignoreNotify == false && item.ExistInTheScene)
            {
                OnItemRepositioned?.Invoke(item);
            }
        }
        private void OnEachItemPositionReverted(InventoryItem item, bool ignoreNotify = false)
        {
            if (ignoreNotify == false && item.ExistInTheScene)
            {
                OnItemPositionReverted?.Invoke(item);
            }
        }
        private void OnEachItemChanged(InventoryItem item, bool ignoreNotify)
        {
            if (ignoreNotify == false && item.ExistInTheScene)
            {
                OnItemChanged?.Invoke(item);
            }
        }

        private void OnEachItemDestroying(InventoryItem item, bool ignoreNotify = false)
        {
            currentItems.Remove(item);
            if (ignoreNotify == false && item.ExistInTheScene)
            {
                OnItemRemoved?.Invoke(item);
            }
        }

        private bool CheckPlacability(InventoryItem item, bool ignoreFloorChecking)
        {
            LayerMask targetLayerMask = GetCollidingLayerMask(item);
            var itemBounds = item.Bounds;
            List<Collider> collidedAssets = Physics.OverlapBox(itemBounds.center, itemBounds.extents,
                Quaternion.identity, targetLayerMask,QueryTriggerInteraction.Ignore).ToList();

            if (ignoreFloorChecking == false)
            {
                if (Raycast(item.ItemType, out RaycastHit hit))
                {
                    collidedAssets.RemoveAll(r => r.gameObject == hit.collider.gameObject);
                }
                else
                {
                    return false;
                }   
            }

          
                collidedAssets.RemoveAll(r => r.gameObject == item.gameObject);
                collidedAssets.RemoveAll(r => r.attachedRigidbody != null && r.attachedRigidbody == item.AttachedRigidBody);
            
            if (item.ItemType == InventoryItemType.Wall)
            {
                var colls = new List<Collider>();
                foreach (var collider in collidedAssets)
                {
                    if(collider.gameObject.layer == LayerMask.NameToLayer("Wall"))
                    {
                        colls.Add(collider);
                    }
                }

                var count = colls.Count;
               collidedAssets = collidedAssets.Except(colls).ToList();
                return collidedAssets.Count() == 0;

            }
            return collidedAssets.Count == 0;
        }

        public bool IsBoundsIncludeBounds(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB)
        {
            return minB.x >= minA.x && minB.x <= maxA.x &&
                   minB.y >= minA.y && minB.y <= maxA.y &&
                   minB.z >= minA.z && minB.z <= maxA.z &&
                   maxB.x >= minA.x && maxB.x <= maxA.x &&
                   maxB.y >= minA.y && maxB.y <= maxA.y &&
                   maxB.z >= minA.z && maxB.z <= maxA.z;
        }

        public HitDataValueObject? CalculateAssetPosition(InventoryItem item, Vector2 pointerPosition)
        {
            return CalculateAssetPosition(item.ItemType, pointerPosition);
        }
        
        public HitDataValueObject? CalculateAssetPosition(InventoryItemType itemType, Vector2 pointerPosition)
        {
            // if (EventSystem.current.IsPointerOverGameObject())
            // {
            //     return null;
            // }
            if (Raycast(itemType, pointerPosition, out RaycastHit hit))
            {
                return new HitDataValueObject{HitPoint = hit.point,Rotation = hit.collider.transform.rotation};
            }

            return null;
            // return Camera.main.ScreenToWorldPoint(pointerPosition);
        }

        private bool Raycast(out RaycastHit hit)
        {
            return Raycast(~0, null, out hit);
        }
        
        private bool Raycast(LayerMask layerMask, int? layer, out RaycastHit hit)
        {
            return Raycast(layerMask, layer,null, Input.mousePosition, out hit);
        }
        
        private bool Raycast(InventoryItemType itemType, out RaycastHit hit)
        {
            return Raycast(itemType, Input.mousePosition, out hit);
        }

        private bool Raycast(InventoryItemType itemType, Vector2 pointerPosition, out RaycastHit hit)
        {
            LayerMask layerMask = config.GetLayerMaskByType(itemType);
            if (itemType == InventoryItemType.AddedOnTop)
            {
                return Raycast(layerMask, null,config.CanBeAddedCollidindLayerMask, pointerPosition, out hit);
            }
            else
            {
                int placeOnLayer = config.GetLayerByType(itemType);
                return Raycast(layerMask, placeOnLayer,null, pointerPosition, out hit);
            }
        }
        
        private bool Raycast(LayerMask layerMask,int? placeOnLayer, LayerMask? placeOnLayerMask, Vector2 pointerPosition, out RaycastHit hit)
        {
            var ray = Camera.main.ScreenPointToRay(pointerPosition);
            if (placeOnLayer == null && placeOnLayerMask == null)
            {
                return Physics.Raycast(ray, out hit, config.MaxCameraDistance, layerMask, QueryTriggerInteraction.Ignore);
            }
            RaycastHit?[] allHits = Physics.RaycastAll(ray, config.MaxCameraDistance, layerMask, QueryTriggerInteraction.Ignore).Select(s => (RaycastHit?)s).ToArray();
            if (allHits.Length == 0)
            {
                hit = new RaycastHit();
                return false;
            }

            var filteredHits = allHits.Cast<RaycastHit?>();
            if (placeOnLayer != null)
            {
                 filteredHits = allHits.Where(f => f.Value.collider.gameObject.layer == placeOnLayer);
            }
            else
            {
                 filteredHits = allHits.Where(f =>placeOnLayerMask == (placeOnLayerMask | (1 << f.Value.collider.gameObject.layer)));
            }            


            var raycastHits = filteredHits as RaycastHit?[] ?? filteredHits.ToArray();
            if (raycastHits.Length == 0)
            {
                hit = new RaycastHit();
                return false;
            }
            hit = raycastHits.OrderBy(f => f.Value.distance).First().Value;
            return true;
        }

        private LayerMask GetCollidingLayerMask(InventoryItem item)
        {
            LayerMask targetLayerMask = new LayerMask();
            switch (item.ItemType)
            {
                case InventoryItemType.Floor:
                    targetLayerMask = item.CarpetBehaviour
                            ? config.CarpetCollidingLayerMask
                            : config.GroundAssetCollidingLayerMask;
                        break;
                case InventoryItemType.Wall:
                    targetLayerMask = config.WallAssetCollidingLayerMask;
                    break;
                case InventoryItemType.AddedOnTop:
                    targetLayerMask = config.CanBeAddedCollidindLayerMask;
                    break;
            }

            return targetLayerMask;
        }

        public struct HitDataValueObject
        {
            public Vector3 HitPoint { get; set; }
            public Quaternion Rotation { get; set; }
        }
    }
}
