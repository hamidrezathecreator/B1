using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Misc.Grid
{
    [RequireComponent(typeof(GridLayoutGroup), typeof(RectTransform))]
    [ExecuteInEditMode]
    public class ResponsiveGrid : MonoBehaviour, IGrid
    {
        private GridLayoutGroup grid;
        public GridLayoutGroup gridLayout { get { return grid != null ? grid : grid = GetComponent<GridLayoutGroup>(); } }
        private RectTransform rect;
        [SerializeField]
        private RectTransform itemSizeDummy;
        [SerializeField]
        private GridDirection direction = GridDirection.Vertical;
        [SerializeField]
        private RectOffsetFloat paddingRatio;
        [SerializeField]
        private Vector2 spacingRatio;

        [SerializeField] private List<RectTransform> constantItems;
        public GridDirection Direction { get { return direction; } }
        private bool initialized = false;
        //[SerializeField]
        //private int extraSpace = 6;
        public event GridEvents.ItemCountChangeEvent onItemCountChanged;
        public int currentItemCount => rect.childCount - constantItems.Count;
        private IEnumerator Start()

        {
            yield return null;
            UpdateGrid();
        }

        private void OnValidate()
        {
            Initialize();
            if (grid == null || rect == null || itemSizeDummy == null)
            {
                return;
            }

            for (int i = 0; i < constantItems.Count; i++)
            {
                if (constantItems[i].parent != transform)
                {
                    constantItems.RemoveAt(i);
                    i--;
                }
            }
            UpdateGrid();
        }

        private void Initialize()
        {
            grid = GetComponent<GridLayoutGroup>();
            rect = transform as RectTransform;
            initialized = true;        
        }

        public void AddItem(GameObject newItem)
        {
            AddItem(newItem, currentItemCount);
        }

        public void PrependItem(GameObject newItem)
        {
            AddItem(newItem, 0);
        }

        public void AddItem(GameObject newItem, int index)
        {
            if (initialized == false)
            {
                Initialize();
            }
            newItem.transform.SetParent(rect, false);
            newItem.transform.SetSiblingIndex(index);
            UpdateGrid();
        }

        public void RemoveItem(GameObject itemToRemove)
        {
            Destroy(itemToRemove);
            UpdateGrid();
        }
        public void SendToTop(GameObject item)
        {
            if (initialized == false)
            {
                Initialize();
            }
            item.transform.SetSiblingIndex(0);
            UpdateGrid();
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (initialized == false)
            {
                Initialize();
            }
            foreach (RectTransform child in rect)
            {
                if (constantItems.Contains(child))
                {
                    continue;
                }
                Destroy(child.gameObject);
            }
            UpdateGrid();
        }
        [ContextMenu("Update Grid")]
        private void UpdateGrid()
        {
            if (initialized == false)
            {
                Initialize();
            }
            grid.cellSize = new Vector2(itemSizeDummy.rect.width,
                itemSizeDummy.rect.height);
            RectOffset padding = new RectOffset();
            padding.left = (int)(paddingRatio.left * itemSizeDummy.rect.width);
            padding.right = (int)(paddingRatio.right * itemSizeDummy.rect.width);
            padding.top = (int)(paddingRatio.top * itemSizeDummy.rect.height);
            padding.bottom = (int)(paddingRatio.bottom * itemSizeDummy.rect.height);
            grid.padding = padding;
            Vector2 spacing;
            spacing.x = spacingRatio.x * (int)itemSizeDummy.rect.width;
            spacing.y = spacingRatio.y * (int)itemSizeDummy.rect.height;
            grid.spacing = spacing;
            //if (direction == GridDirection.Vertical)
            //{
            //    rect.SetSizeWithCurrentAnchors(
            //    RectTransform.Axis.Vertical, itemSizeDummy.rect.height * rect.childCount + grid.spacing.y * rect.childCount - 1);
            //}
            //else if (direction == GridDirection.Horizontal)
            //{
            //    rect.SetSizeWithCurrentAnchors(
            //        RectTransform.Axis.Horizontal, itemSizeDummy.rect.width * rect.childCount + grid.spacing.x * rect.childCount - 1);
            //}
            grid.enabled = true;
            onItemCountChanged?.Invoke(rect.childCount);
        }

        private void OnGUI()
        {
            Initialize();
            if (grid == null || rect == null || itemSizeDummy == null)
            {
                return;
            }
            UpdateGrid();
        }
        [Serializable]
        public class RectOffsetFloat
        {
            public float left;
            public float right;
            public float top;
            public float bottom;
        }
    }
}
