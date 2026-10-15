using Animation.InteractionHelpers;
using UnityEngine;
using UnityEngine.UI;

namespace Animation.Inventory
{
    public class UIInventoryItem : UIInputBehaviour
    {
        [SerializeField]
        private Text titleLabel;
        [SerializeField]
        private RawImage iconRawImage;
        public InventoryItem RelatedItem { get; set; }
        public void Initialize(InventoryItem relatedItem,string buttonTitle = null,Texture2D itemIcon = null)
        {
            if (buttonTitle != null)
            {
                titleLabel.text = buttonTitle;
            }
            if (itemIcon != null)
            {
                this.iconRawImage.texture = itemIcon;
            }
            this.RelatedItem = relatedItem;
        }
    }
}