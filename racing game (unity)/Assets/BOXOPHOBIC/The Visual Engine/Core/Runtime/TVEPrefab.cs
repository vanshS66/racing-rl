// Cristian Pop - https://boxophobic.com/

using UnityEngine;
using Boxophobic.StyledGUI;

namespace TheVisualEngine
{
#if UNITY_EDITOR
    [HelpURL("https://docs.google.com/document/d/145JOVlJ1tE-WODW45YoJ6Ixg23mFc56EnB_8Tbwloz8/edit#heading=h.q4fstlrr3cw4")]
    [ExecuteInEditMode]
    [AddComponentMenu("BOXOPHOBIC/The Visual Engine/TVE Prefab")]
#endif
    public class TVEPrefab : StyledMonoBehaviour
    {
#if UNITY_EDITOR
        [StyledBanner(0.9f, 0.7f, 0.4f, "Prefab")]
        public bool styledBanner;

        [HideInInspector]
        public bool lockInAssetConverter;

#if !THE_VISUAL_ENGINE_DEBUG
        [HideInInspector]
#endif
        public string storedPrefabBackupGUID = "";

#if !THE_VISUAL_ENGINE_DEBUG
        [HideInInspector]
#endif
        public string storedPrefabParentGUID = "";

#if !THE_VISUAL_ENGINE_DEBUG
        [HideInInspector]
#endif
        public string storedPreset = "";

#if !THE_VISUAL_ENGINE_DEBUG
        [HideInInspector]
#endif
        public string storedOverrides = "";
#endif

        // [ContextMenu("Lock In Asset Converter")]
        // void LockPrefab()
        // {
        //     lockInAssetConverter = true;
        // }

        // [ContextMenu("Unlock In Asset Converter")]
        // void UnlockPrefab()
        // {
        //     lockInAssetConverter = false;
        // }
    }
}




