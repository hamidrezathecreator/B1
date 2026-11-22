using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering;

/// <summary>
/// XRI affordances expect a renderer on a parent mesh. Controller poke affordances and hidden
/// controller visuals break that; bind a renderer or switch the affordance off before OnEnable.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class XrAffordanceRendererFix : MonoBehaviour
{
    void Awake() => XrAffordanceUtility.FixAffordances(transform);
}

public static class XrAffordanceUtility
{
    public static void FixAffordances(Transform root)
    {
        if (root == null) return;

        foreach (var helper in root.GetComponentsInChildren<MaterialPropertyBlockHelper>(true))
            FixMaterialHelper(helper);
    }

    public static void FixSceneAffordances()
    {
        foreach (var helper in Object.FindObjectsByType<MaterialPropertyBlockHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            FixMaterialHelper(helper);
    }

    static void FixMaterialHelper(MaterialPropertyBlockHelper helper)
    {
        if (helper == null) return;

        if (helper.rendererTarget == null)
            helper.rendererTarget = ResolveRenderer(helper.transform);

        if (helper.rendererTarget == null)
        {
            SetAffordanceBranchOff(helper.gameObject);
            return;
        }

        if (!helper.enabled) return;
        helper.enabled = false;
        helper.enabled = true;
    }

    static void SetAffordanceBranchOff(GameObject affordanceRoot)
    {
        if (affordanceRoot == null) return;
        foreach (var helper in affordanceRoot.GetComponents<MaterialPropertyBlockHelper>())
            helper.enabled = false;
        foreach (var receiver in affordanceRoot.GetComponents<MonoBehaviour>())
        {
            if (receiver == null) continue;
            var name = receiver.GetType().Name;
            if (name.Contains("ColorAffordance") || name.Contains("BlendShapeAffordance"))
                receiver.enabled = false;
        }
    }

    static Renderer ResolveRenderer(Transform affordanceTransform)
    {
        var controller = FindControllerRoot(affordanceTransform);
        if (controller != null)
        {
            var projectHand = controller.Find("ProjectHand");
            if (projectHand == null)
            {
                foreach (var child in controller.GetComponentsInChildren<Transform>(true))
                {
                    if (!child.name.StartsWith("ProjectHand")) continue;
                    projectHand = child;
                    break;
                }
            }

            if (projectHand != null)
            {
                var handMesh = projectHand.GetComponentInChildren<Renderer>(true);
                if (handMesh != null) return handMesh;
            }

            foreach (var r in controller.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("Poke Point") || r.name.Contains("UniversalController") ||
                    r.name.Contains("Controller Visual"))
                    return r;
            }

            foreach (var r in controller.GetComponentsInChildren<Renderer>(true))
                return r;
        }

        foreach (var r in affordanceTransform.GetComponentsInParent<Renderer>(true))
            return r;

        return null;
    }

    static Transform FindControllerRoot(Transform start)
    {
        for (var t = start; t != null; t = t.parent)
        {
            if (t.name == "Left Controller" || t.name == "Right Controller")
                return t;
        }

        return null;
    }
}