using System;
using System.Linq;
using Animation.InteractionHelpers;
using UnityEngine;

namespace Animation.Inventory
{
    public class InventoryItem : ColliderInputBehaviour
    {
        // public string ID { get; private set; }
        [SerializeField] private string title;
        [SerializeField] private InventoryItemType itemType = InventoryItemType.Floor;
        internal InventoryValidationPointer ValidationPointer { get; set; }
        public string Title
        {
            get => title;
            set => title = value;
        }
        
        public Vector3 CurrentPosition { get { return transform.position; } }
        public Quaternion CurrentRotation { get { return transform.rotation; } }

        private Material[] _materials;
        private Material[] materials { get { return _materials != null ? _materials : _materials = GetComponentsInChildren<Renderer>().Select(s => s.material).ToArray(); } }
        private Color[] defaultMaterialColors = new Color[] { };
        private bool isPlaceable = false;
        public Vector3? LastPlacedPosition { get; private set; }
        public Quaternion? LastPlacedRotation { get; private set; }
        public bool ExistInTheScene { get; private set; } = false;
        private bool isPositionInitialized = false;
        private Vector3 rotationAddition = Vector3.zero;

        public bool IsPlaceable
        {
            get => isPlaceable;
            set
            {
                if (isPlaceable == value)
                {
                    return;
                }
                isPlaceable = value;
                OnChanged?.Invoke(this, false);
            }
        }
        [SerializeField]
        private Vector3 offset;
        [SerializeField]
        private bool carpetBehaviour;

        public bool CarpetBehaviour
        {
            get
            {
                return carpetBehaviour;
            }
        }
        [SerializeField]
        private bool canGoOnTopOfOtherAssets;

        public bool CanGoOnTopOfOtherAssets
        {
            get
            {
                return canGoOnTopOfOtherAssets;
            }
        }
        public Bounds Bounds
        {
            get
            {
                return GetBounds(gameObject);
            }
        }
        public Bounds[] ColliderBounds
        {
            get
            {
                BoxCollider[] colliders = GetComponentsInChildren<BoxCollider>();
                if (colliders == null)
                {
                    return new Bounds[] { };
                }
                return colliders.Select(s => s.bounds).ToArray();
            }
        }


        public InventoryItemType ItemType
        {
            get => itemType;
            set => itemType = value;
        }

        private InventoryItemStatus currentStatus = InventoryItemStatus.None;

        public InventoryItemStatus CurrentStatus
        {
            get => currentStatus;
            private set
            {
                if (currentStatus == value)
                {
                    return;
                }
                currentStatus = value;
                OnChanged?.Invoke(this, false);
                switch (value)
                {
                    case InventoryItemStatus.Placed:
                    case InventoryItemStatus.Selected:
                        thisRigidbody = GetComponent<Rigidbody>();
                        if (thisRigidbody == null)
                        {
                            tempRigidbody = gameObject.AddComponent<Rigidbody>();
                            tempRigidbody.isKinematic = true;
                            tempRigidbody.useGravity = false;
                        }
                        break;
                }

                if (ValidationPointer != null)
                {
                    if (value == InventoryItemStatus.Selected)
                    {
                        ValidationPointer.SetInventoryItem(this);
                        ValidationPointer.SetActive(true);   
                    }
                    else
                    {
                        ValidationPointer.SetActive(false);   
                    }   
                }
            }
        }
        
        public event ItemNotifyDelegate OnChanged;
        public event InventoryItemDelegate OnCloned;
        public event ItemNotifyDelegate OnDestroying;
        public event ItemNotifyDelegate OnPlaced;
        public event ItemNotifyDelegate OnRepositioned;
        public event ItemNotifyDelegate OnPositionReverted;
        internal Func<InventoryItem, bool, bool> CheckPlacabilityFunc { private get; set; }
        internal Func<InventoryItem, Vector2, InventoryManager.HitDataValueObject?> CalculatePositionFunc { private get; set; }
        private Rigidbody thisRigidbody;
        private Rigidbody tempRigidbody;
        public Rigidbody AttachedRigidBody => thisRigidbody != null ? thisRigidbody : tempRigidbody;

        protected override void OnStartDraggingAction()
        {
            if (ExistInTheScene == false)
            {
                return;
            }
            CurrentStatus = InventoryItemStatus.Selected;
        }
        protected override void OnDraggingAction()
        {
            if (ExistInTheScene == false || Interactable == false)
            {
                return;
            }

            InventoryManager.HitDataValueObject? targetTransform = CalculateAssetPosition(CurrentPointerPosition);
            if (targetTransform != null)
            {
                var trans = (InventoryManager.HitDataValueObject)targetTransform;
                //if (itemType == InventoryItemType.Wall)
                //{
                    var rot = trans.Rotation * Quaternion.Euler(offset);
                    SetRotation(rot);
                //}

                
                MoveItem(trans.HitPoint);
            }
            else if (isPositionInitialized == false)
            {
                if(ItemType == InventoryItemType.Wall){return;}
                var pos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                MoveItem(pos);
            }
        }

        protected override void OnFinishDraggingAction()
        {
            if (ExistInTheScene == false)
            {
                return;
            }
            if (PlaceItem() == false)
            {
                RevertPosition();
            }
        }

        public InventoryItem CloneItem(Vector3? initialPosition = null, Quaternion? initialRotation = null, bool autoPlace = false)
        {
            InventoryItem newItem;
            switch (CurrentStatus)
            {
                case InventoryItemStatus.None:
                case InventoryItemStatus.Placed:
                    newItem = Instantiate(this);
                    var thisTransform = transform;
                    var newItemTrans = newItem.transform;
                    newItemTrans.position = initialPosition ?? (ExistInTheScene ? thisTransform.position : Vector3.one * -10000f);
                    newItemTrans.rotation = initialRotation ?? (ExistInTheScene ? thisTransform.rotation : Quaternion.identity);
                    // newItem.ID = GenerateID();
                    newItem.CurrentStatus = InventoryItemStatus.Selected;
                    OnCloned?.Invoke(newItem);
                    if (newItem.ValidationPointer != null)
                    {
                        newItem.ValidationPointer.SetInventoryItem(newItem);
                        newItem.ValidationPointer.SetActive(true);
                    }
                    if (autoPlace)
                    {
                        if (newItem.PlaceItem(true, true) == false)
                        {
                            newItem.DestoryItem();
                            return null;
                        }
                    }
                    newItem.gameObject.SetActive(true);
                    return newItem;
                default:
                    Debug.LogError("Cannot clone item on current status");
                    return null;
            }
        }

        public void SelectItem()
        {
            /*if (CurrentStatus == InventoryItemStatus.None)
            {
                Debug.LogError("You cannot select an inventory item prefab");
                return;
            }*/
            CurrentStatus = InventoryItemStatus.Selected;
        }

        public bool PlaceItem(bool ignoreNotify = false, bool force = false)
        {
            IsPlaceable = force || (CheckPlacabilityFunc?.Invoke(this, false) ?? false);
            if (IsPlaceable == false)
            {
                return false;
            }
            CurrentStatus = InventoryItemStatus.Placed;
            LastPlacedPosition = transform.position;
            LastPlacedRotation = transform.rotation;
            var alreadyExist = ExistInTheScene;
            ExistInTheScene = true;
            if (alreadyExist)
            {
                OnRepositioned?.Invoke(this, ignoreNotify);
            }
            else
            {
                OnPlaced?.Invoke(this, ignoreNotify);
            }
            return true;
        }

        public void RevertPosition(bool ignoreNotify = false)
        {
            if (LastPlacedPosition == null || LastPlacedRotation == null || ExistInTheScene == false)
            {
                return;
            }
            SetPosition((Vector3)LastPlacedPosition);
            transform.rotation = (Quaternion)LastPlacedRotation;
            OnPositionReverted?.Invoke(this,ignoreNotify);
            if (ValidationPointer != null)
            {
                ValidationPointer.SetActive(false);
            }
        }

        public bool DestoryItem(bool ignoreNotify = false)
        {
            switch (CurrentStatus)
            {
                case InventoryItemStatus.None:
                    Debug.LogError("You cannot destroy an inventory item prefab");
                    return false;
                default:
                    OnDestroying?.Invoke(this, ignoreNotify);
                    Destroy(gameObject);
                    if (ValidationPointer != null)
                    {
                        ValidationPointer.SetActive(false);
                    }
                    return true;
            }
        }

        public void MoveItem(Vector3 position, bool ignoreNotify = false)
        {
            /*if (CurrentStatus != InventoryItemStatus.Selected)
            {
                Debug.LogError("You cannot move a deselected item");
                return;
            }*/
            IsPlaceable = CheckPlacabilityFunc?.Invoke(this, false) ?? false;
            SetPosition(position);
            OnChanged?.Invoke(this, ignoreNotify);
        }
        
        [ContextMenu("RotateRight")]
        public void RotateRight(bool ignoreNotify = false)
        {
            if (itemType == InventoryItemType.Wall)
            {
                Debug.Log("Cant Rotate Wall Assets");
                return;
            }
            SelectItem();
            RotateItem(90f, ignoreNotify);
        }
        
        [ContextMenu("RotateLeft")]
        public void RotateLeft(bool ignoreNotify = false)
        {
            if (itemType == InventoryItemType.Wall)
            {
                Debug.Log("Cant Rotate Wall Assets");
                return;
            }
            SelectItem();
            RotateItem(-90f, ignoreNotify);
        }

        public void RotateItem(float rotationDegree, bool ignoreNotify = false)
        {
            /*if (CurrentStatus != InventoryItemStatus.Selected)
            {
                Debug.LogError("You cannot rotate a deselected item");
                return;
            }*/

            rotationAddition.y += rotationDegree;
            RotateItem(transform.rotation, ignoreNotify);
        }

        public void RotateItem(Quaternion newRotation, bool ignoreNotify = false)
        {
            /*if (CurrentStatus != InventoryItemStatus.Selected)
            {
                Debug.LogError("You cannot rotate a deselected item");
                return;
            }*/
            newRotation *= Quaternion.Euler(offset);
            SetRotation(Quaternion.Euler(newRotation.eulerAngles + rotationAddition));
            IsPlaceable = CheckPlacabilityFunc?.Invoke(this, true) ?? false;
            OnChanged?.Invoke(this, ignoreNotify);
            rotationAddition = Vector3.zero;
            if (isPlaceable)
            {
                PlaceItem(ignoreNotify, true);
            }
        }
        
        private Bounds GetBounds(GameObject go)
        {
            Bounds bounds;
            Renderer childRenderer;
            bounds = GetRenderBounds(go);
            if(bounds.extents.x == 0)
            {
                bounds = new Bounds(go.transform.position,Vector3.zero);
                foreach (Transform child in go.transform)
                {
                    childRenderer = child.GetComponent<Renderer>();
                    if (childRenderer) 
                    {
                        bounds.Encapsulate(childRenderer.bounds);
                    }else
                    {
                        bounds.Encapsulate(GetBounds(child.gameObject));
                    }
                }
            }
            return bounds;
        }
        private Bounds GetRenderBounds(GameObject go)
        {
            var bounds = new  Bounds(Vector3.zero,Vector3.zero);
            var render = go.GetComponent<Renderer>();
            if(render!=null)
            {
                return render.bounds;
            }
            return bounds;
        }

        public InventoryManager.HitDataValueObject? CalculateAssetPosition(Vector2 pointerPosition)
        {
            return CalculatePositionFunc?.Invoke(this, pointerPosition) ?? null;
        }

        private void SetPosition(Vector3 position)
        {
            transform.position = position;
            isPositionInitialized = true;
        }

        private void SetRotation(Quaternion rotation)
        {
            transform.rotation = rotation;
        }

        // private string GenerateID()
        // {
        //     return GUID.Generate().ToString();
        // }
        public enum InventoryItemStatus{ None = 0, Selected = 1, Placed = 2}
        public delegate void InventoryItemDelegate(InventoryItem item);
        public delegate void ItemNotifyDelegate(InventoryItem item, bool ignoreNotify);
    }
   
}