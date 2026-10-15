using System;
using UnityEngine;

namespace Animation.Inventory
{
    [Serializable]
    public class InventoryConfig
    {
        [SerializeField] private LayerMask floorLayerMask;
        [SerializeField] private LayerMask wallLayerMask;
        [SerializeField] private LayerMask canBeAddedAssetsLayerMask;

        [SerializeField] private LayerMask carpetCollidingLayerMask;
        [SerializeField] private LayerMask groundAssetCollidingLayerMask;
        [SerializeField] private LayerMask wallAssetCollidingLayerMask;
        [SerializeField] private LayerMask canBeAddedAssetsCollidingLayerMask;
        [SerializeField] private float maxCameraDistance = 1000f;
        [SerializeField] private string floorLayer = "Floor";
        [SerializeField] private string canBeAddedLayer = "CanBeAddedToAssetsLayer";
        [SerializeField] private string wallLayer = "Wall";
        
        public LayerMask FloorLayerMask => floorLayerMask;
        public LayerMask WallLayerMask => wallLayerMask;
        public LayerMask CanBeAddedCollidindLayerMask => canBeAddedAssetsCollidingLayerMask;
        public LayerMask CarpetCollidingLayerMask => carpetCollidingLayerMask;
        public LayerMask GroundAssetCollidingLayerMask => groundAssetCollidingLayerMask;
        public LayerMask WallAssetCollidingLayerMask => wallAssetCollidingLayerMask;
        public float MaxCameraDistance => maxCameraDistance;

        public int FloorLayer => LayerMask.NameToLayer(floorLayer);
        public int CanBeAddedLayer => LayerMask.NameToLayer(canBeAddedLayer);//CanBeAddedLayerMask;//LayerMask.NameToLayer(canBeAddedLayer);
        public int WallLayer => LayerMask.NameToLayer(wallLayer);

        public int GetLayerByType(InventoryItemType itemType)
        {
            switch (itemType)
            {
                case InventoryItemType.Floor:
                    return FloorLayer;
                case InventoryItemType.Wall:
                    return WallLayer;
                case InventoryItemType.AddedOnTop:
                    return CanBeAddedLayer;
                default:
                    return 0;
            }
        }

        public LayerMask GetLayerMaskByType(InventoryItemType itemType)
        {
            switch (itemType)
            {
                case InventoryItemType.Floor:
                    return floorLayerMask;
                case InventoryItemType.Wall:
                    return wallLayerMask;
                case InventoryItemType.AddedOnTop:
                    return canBeAddedAssetsLayerMask;
                default:
                    return new LayerMask();
            }
        }
        
    }
}