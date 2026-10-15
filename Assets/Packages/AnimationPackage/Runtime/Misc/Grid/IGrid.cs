using UnityEngine;
using UnityEngine.UI;

namespace Animation.Misc.Grid
{
    public interface IGrid
    {
        void AddItem(GameObject newItem);
        void Clear();
        int currentItemCount { get; }
        event GridEvents.ItemCountChangeEvent onItemCountChanged;
        GridDirection Direction { get; }
        GridLayoutGroup gridLayout { get; }
    }
}
