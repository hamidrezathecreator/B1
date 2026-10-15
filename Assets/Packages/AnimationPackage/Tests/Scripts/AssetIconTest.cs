using Animation.ScreenShot;
using UnityEngine;
using UnityEngine.UI;

public class AssetIconTest : MonoBehaviour
{
    [SerializeField]
    private RawImage rawImage;
    [SerializeField]
    private ScreenShotGetter screenShotGetter;
    [SerializeField]
    private GameObject assetObject;
    [SerializeField]
    private Vector3 AssetPos;
    [SerializeField]
    private Vector3 AssetRot;
    void Start()
    {
        rawImage.texture = screenShotGetter.GetScreenShot(assetObject,Vector3.zero, Vector3.zero,true);
    }
}