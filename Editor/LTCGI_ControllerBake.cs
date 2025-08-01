#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#endif

namespace pi.LTCGI
{
    #if UNITY_EDITOR
    public partial class LTCGI_Controller
    {
        [Header("Lightmap Baking Cache (do not edit!)")]
        [SerializeField] internal List<Material> bakeMaterialReset_key;
        [SerializeField] private List<MaterialGlobalIlluminationFlags> bakeMaterialReset_val;
        [SerializeField] public bool bakeInProgress;
        [SerializeField] private LightingDataAsset prevLightmapData;
        [SerializeField] private Texture2D[] prevLightmaps0;
        [SerializeField] private Texture2D[] prevLightmaps1;
        [SerializeField] private Texture2D[] prevLightmaps2;
        [SerializeField] private Texture2D[] prevLightmaps3;
        [SerializeField] private LightmapsMode prevLightmapMode;

        [SerializeField] private Texture2D[] _LTCGI_Lightmaps;
        [SerializeField] private Renderer[] _LTCGI_LightmapData_key;
        [SerializeField] private Vector4[] _LTCGI_LightmapOffsets_val;
        [SerializeField] private int[] _LTCGI_LightmapIndex_val;

        [SerializeField] private AmbientMode previousAmbientMode;
        [SerializeField] private float previousAmbientIntensity;
        [SerializeField] private Color previousAmbientColor;

        [SerializeField] private bool followupWithRealBake;
        [SerializeField] private bool followupBakery;
        [SerializeField] private bool allowStaticBatching = false;

        public static event Action OnLTCGIShadowmapBakeComplete;
        public static event Action OnLTCGIShadowmapClearData;

        public bool HasLightmapData() => _LTCGI_Lightmaps != null && _LTCGI_Lightmaps.Length > 0;
        public void ClearLightmapData()
        {
            _LTCGI_Lightmaps = null;
            _LTCGI_LightmapData_key = null;
            _LTCGI_LightmapOffsets_val = null;
            _LTCGI_LightmapIndex_val = null;

            OnLTCGIShadowmapClearData?.Invoke();
        }

        [MenuItem("Tools/LTCGI/Bake Shadowmap")]
        public static void BakeLightmap()
        {
            var bakery = false;
            #if BAKERY_INCLUDED
                bakery = EditorUtility.DisplayDialog("LTCGI", "Bakery has been detected in your project. Do you want to bake the lightmap with Bakery?", "Yes, use Bakery", "No, use built-in");
            #endif
            LTCGI_Controller.Singleton.BakeLightmap(bakery, false);
        }

        [MenuItem("Tools/LTCGI/Bake Shadowmap and Normal Lightmap")]
        public static void BakeLightmapFollowup()
        {
            var bakery = false;
            #if BAKERY_INCLUDED
                bakery = EditorUtility.DisplayDialog("LTCGI", "Bakery has been detected in your project. Do you want to bake the lightmap with Bakery?", "Yes, use Bakery", "No, use built-in");
            #endif
            LTCGI_Controller.Singleton.BakeLightmap(bakery, true);
        }

        internal void BakeLightmap(bool bakery, bool followup)
        {
            if (Lightmapping.isRunning
            #if BAKERY_INCLUDED
                || ftRenderLightmap.bakeInProgress
            #endif
            ) {
                Debug.Log("A lightmapping job is already running. Try again when it is done.");
                return;
            }

            #if !BAKERY_INCLUDED
                bakery = false;
            #endif

            followupWithRealBake = followup;
            followupBakery = bakery;

            LTCGI_Controller.Singleton.UpdateMaterials();
            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

            EditorUtility.DisplayProgressBar("Preparing LTCGI bake", "Disabling all external GI contributors", 0.0f);

            bakeMaterialReset_key = new List<Material>();
            bakeMaterialReset_val = new List<MaterialGlobalIlluminationFlags>();

            // disable all other contributors
            var allRenderers = GameObject.FindObjectsOfType<Renderer>();
            foreach (var renderer in allRenderers)
            {
                foreach (var m in renderer.sharedMaterials)
                {
                    if (m != null && !bakeMaterialReset_key.Contains(m))
                    {
                        bakeMaterialReset_key.Add(m);
                        bakeMaterialReset_val.Add(m.globalIlluminationFlags);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    }
                }
            }

            var bakeResets = new Dictionary<GameObject, LTCGI_BakeReset>();
            LTCGI_BakeReset resetter(GameObject obj)
            {
                if (bakeResets.ContainsKey(obj)) return bakeResets[obj];
                var bakeReset = obj.gameObject.AddComponent<LTCGI_BakeReset>();
                bakeResets.Add(obj, bakeReset);
                return bakeReset;
            }

            var allLights = GameObject.FindObjectsOfType<Light>();
            foreach (var light in allLights)
            {
                if (light.gameObject.activeSelf)
                {
                    light.gameObject.SetActive(false);
                    var r = resetter(light.gameObject);
                    r.Reenable = true;
                }
            }

            #if BAKERY_INCLUDED
            var allBakeryLights =
                GameObject.FindObjectsOfType<BakerySkyLight>().Select(x => x.gameObject)
                .Concat(GameObject.FindObjectsOfType<BakeryPointLight>().Select(x => x.gameObject))
                .Concat(GameObject.FindObjectsOfType<BakeryLightMesh>().Select(x => x.gameObject))
                .Concat(GameObject.FindObjectsOfType<BakeryDirectLight>().Select(x => x.gameObject));
            foreach (var light in allBakeryLights)
            {
                if (light.activeSelf && (!light.TryGetComponent<BakeryLightMesh>(out _) || !light.TryGetComponent<LTCGI_Screen>(out _)))
                {
                    light.SetActive(false);
                    var r = resetter(light.gameObject);
                    r.Reenable = true;
                }
            }
            #endif

            var allReflProbes = GameObject.FindObjectsOfType<ReflectionProbe>();
            foreach (var reflProbe in allReflProbes)
            {
                if (reflProbe.gameObject.activeSelf)
                {
                    reflProbe.gameObject.SetActive(false);
                    var r = resetter(reflProbe.gameObject);
                    r.Reenable = true;
                }
            }

            // configure lighting to exclude skybox/env lighting
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientIntensity = RenderSettings.ambientIntensity;
            previousAmbientColor = RenderSettings.ambientSkyColor;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientIntensity = 0.0f;
            RenderSettings.ambientSkyColor = Color.black;

            EditorUtility.DisplayProgressBar("Preparing LTCGI bake", "Making LTCGI_Screens emissive", 0.5f);

            var foundEnabledScreen = false;

            // make screen emissive
            var allScreens = GameObject.FindObjectsOfType<LTCGI_Screen>();
            foreach (var scr in allScreens)
            {
                if (scr.LightmapChannel == 0 || !scr.enabled) continue;
                var intens = LightmapIntensity * scr.LightmapIntensity;
                var mat = new Material(Shader.Find("Standard"));
                var col = scr.LightmapChannel == 1 ? new Color(intens, 0, 0, 1) : (
                          scr.LightmapChannel == 2 ? new Color(0, intens, 0, 1) : (
                          scr.LightmapChannel == 3 ? new Color(0, 0, intens, 1) :
                          new Color(0, 0, 0, 1)));
                mat.SetColor("_EmissionColor", col);
                mat.EnableKeyword("_EMISSION");
                mat.doubleSidedGI = scr.DoubleSided;
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;

                Action<Renderer> handleRenderer = (rend) => {
                    var flags = GameObjectUtility.GetStaticEditorFlags(rend.gameObject);
                    var r = resetter(rend.gameObject);
                    #if BAKERY_INCLUDED
                    if (rend.TryGetComponent(out BakeryLightMesh lightMesh))
                    {
                        rend.gameObject.SetActive(true);
                        r.ResetLightMesh = true;
                        r.lightMeshColor = lightMesh.color;
                        r.lightMeshIntensity = lightMesh.intensity;
                        lightMesh.color = new Color(col.r / intens, col.g / intens, col.b / intens, 1);
                        lightMesh.intensity = intens;
                    }
                    else
                    #endif
                    {
                        r.ResetData = true;
                        r.Materials = rend.sharedMaterials;
                        r.Flags = flags;
                        r.ShadowCastingMode = rend.shadowCastingMode;
                        if (rend.shadowCastingMode == ShadowCastingMode.Off || rend.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                        {
                            rend.shadowCastingMode = ShadowCastingMode.On;
                        }
                        rend.sharedMaterials = new Material[] { mat };
                        GameObjectUtility.SetStaticEditorFlags(rend.gameObject, flags | StaticEditorFlags.ContributeGI);
                    }

                    if (!rend.enabled)
                    {
                        Debug.LogWarning("LTCGI: An object with an LTCGI_Screen component has a disabled renderer, it will be forcibly enabled for the bake", rend.gameObject);
                        rend.enabled = true;
                        r.DisableRendererComponents = new Renderer[] { rend };
                    }

                    foundEnabledScreen = true;
                };

                LTCGI_Emitter emitter;
                if ((emitter = scr as LTCGI_Emitter) != null)
                {
                    foreach (var rend in emitter.EmissiveRenderers)
                    {
                        handleRenderer(rend);
                    }
                }
                else
                {
                    if (scr.gameObject.TryGetComponent<MeshRenderer>(out MeshRenderer rend))
                    {
                        handleRenderer(rend);
                    }
                    else
                    {
                        /* There is no mesh renderer on this object */
                        Debug.LogWarning("LTCGI: An object with an LTCGI_Screen component has no mesh renderer, it will not contribute shadows to this bake", scr.gameObject);
                    }
                }
            }

            if (!foundEnabledScreen)
            {
                ResetConfiguration();
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("LTCGI", "No LTCGI_Screen components were found that are enabled and have a valid LightmapChannel set. Please configure at least one screen and try again.", "OK");
                return;
            }

            bakeInProgress = true;
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            EditorUtility.ClearProgressBar();

            if (!AssetDatabase.IsValidFolder("Assets/LTCGI-Generated"))
                AssetDatabase.CreateFolder("Assets", "LTCGI-Generated");

            /*prevLightmapData = Lightmapping.lightingDataAsset;
            Lightmapping.lightingDataAsset = null;
            LightmapSettings.lightmaps = null;
            prevLightmapMode = LightmapSettings.lightmapsMode;
            prevLightmaps0 = new Texture2D[LightmapSettings.lightmaps.Length];
            prevLightmaps1 = new Texture2D[LightmapSettings.lightmaps.Length];
            prevLightmaps2 = new Texture2D[LightmapSettings.lightmaps.Length];
            prevLightmaps3 = new Texture2D[LightmapSettings.lightmaps.Length];
            for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
            {
                prevLightmaps0[i] = LightmapSettings.lightmaps[i].lightmapColor;
                prevLightmaps1[i] = LightmapSettings.lightmaps[i].lightmapDir;
                #pragma warning disable 0618
                prevLightmaps2[i] = LightmapSettings.lightmaps[i].lightmapLight;
                #pragma warning restore 0618
                prevLightmaps3[i] = LightmapSettings.lightmaps[i].shadowMask;
            }*/

            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            Debug.Log("LTCGI: Shadowmap bake started");

            //EditorUtility.DisplayDialog("LTCGI bake", "Bake your lightmap now using either the integrated Unity lightmap baking tools or Bakery, and when finished another message should come up. If not, go back to the Controller object and manually click 'Semi-Auto Bake Shadowmap FINISH'.", "OK");

            if (!bakery)
            {
                Lightmapping.bakeCompleted += BakeCompleteEvent;
                EditorUtility.DisplayDialog("LTCGI", "Please don't touch the scene during async bake.", "I promise!");
                Lightmapping.BakeAsync();
            }

            #if BAKERY_INCLUDED
                else if (bakery)
                {
                    ftRenderLightmap.OnFinishedFullRender += BakeCompleteEvent;
                    var b = ftRenderLightmap.instance;
                    if (b == null)
                    {
                        b = ftRenderLightmap.instance = ftRenderLightmap.CreateInstance<ftRenderLightmap>();
                        //EditorUtility.DisplayDialog("LTCGI", "Bakery instance not found, please bake lightmaps manually now.", "OK");
                    }
                    //else
                    {
                        b.Show();
                        b.SaveRenderSettings();
                        b.LoadRenderSettings();
                        // "Asset UV processing" = "Don't change"
                        ftBuildGraphics.unwrapUVs = false;
                        ftBuildGraphics.forceDisableUnwrapUVs = false;
                        b.SaveRenderSettings();
                        EditorApplication.delayCall += () => {
                            b.RenderButton(false);
                        };
                    }
                }
            #endif
        }

        private static void BakeCompleteEvent()
        {
            var obj = GameObject.FindObjectOfType<LTCGI_Controller>();

            if (!obj.bakeInProgress) return;

            Lightmapping.bakeCompleted -= BakeCompleteEvent;
            #if BAKERY_INCLUDED
                ftRenderLightmap.OnFinishedFullRender -= BakeCompleteEvent;
            #endif

            EditorApplication.delayCall += obj.BakeComplete;
        }
        private static void BakeCompleteEvent(object a, EventArgs b) => BakeCompleteEvent();
        internal void BakeComplete()
        {
            try
            {
                BakeCompleteProg();
            }
            finally
            {
                followupWithRealBake = false;

                // avoid stuck progress bar
                EditorUtility.ClearProgressBar();

                // I think this should be safe, and avoid some issues with data not being reset
                ResetConfiguration();
            }
        }
        internal void BakeCompleteProg()
        {
            //EditorUtility.DisplayDialog("LTCGI bake", "Lightmap baking has finished, LTCGI will now apply the generated configuration.", "OK");

            EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Copying calculated lightmaps", 0.0f);

            // move away calculated lightmap assets
            var curscene = EditorSceneManager.GetActiveScene().name;
            AssetDatabase.DeleteAsset("Assets/LTCGI-Generated/Lightmaps-" + curscene);
            AssetDatabase.CreateFolder("Assets/LTCGI-Generated", "Lightmaps-" + curscene);
            var newLightmapPaths = new List<string>();
            for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
            {
                LightmapData lm = LightmapSettings.lightmaps[i];
                EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Copying calculated lightmaps", i/((float)LightmapSettings.lightmaps.Length-1.0f));
                var tex = lm.lightmapColor;
                var path = AssetDatabase.GetAssetPath(tex);
                var newPath = "Assets/LTCGI-Generated/Lightmaps-" + curscene + "/" + System.IO.Path.GetFileName(path);
                AssetDatabase.CopyAsset(path, newPath);
                newLightmapPaths.Add(newPath);
            }
            AssetDatabase.Refresh();

            EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Caching lightmaps", 0.0f);

            // Copy data to LTCGI buffer, so that other bakes don't influence it
            _LTCGI_Lightmaps = new Texture2D[LightmapSettings.lightmaps.Length];
            for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Caching lightmaps", i/((float)LightmapSettings.lightmaps.Length-1.0f));
                var tex = LightmapSettings.lightmaps[i].lightmapColor;
                var path = AssetDatabase.GetAssetPath(tex);
                var tex2 = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/LTCGI-Generated/Lightmaps-" + curscene + "/" + System.IO.Path.GetFileName(path));
                SetTextureImporterToLightmap(tex2);
                _LTCGI_Lightmaps[i] = tex2;
            }

            EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Applying indexed lightmaps to renderers", 0.0f);

            var renderers = GameObject.FindObjectsOfType<Renderer>();
            var rkey = new List<Renderer>();
            var rval = new List<Vector4>();
            var rival = new List<int>();
            for (int i = 0; i < renderers.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Applying indexed lightmaps to renderers", i/((float)renderers.Length-1.0f));
                var r = renderers[i];
                if (GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.ContributeGI))
                {
                    rkey.Add(r);
                    rval.Add(r.lightmapScaleOffset);
                    rival.Add(r.lightmapIndex);

                    if (allowStaticBatching)
                        continue;

                    foreach (var m in r.sharedMaterials)
                    {
                        if (MatLTCGIenabled(m))
                        {
                            // Disable static batching for all objects we double-lightmap, as
                            // otherwise Unity bakes unity_LightmapST into the UV channels and
                            // breaks our custom offsets.
                            var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                            flags &= ~StaticEditorFlags.BatchingStatic;
                            GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags);
                            break;
                        }
                    }
                }
            }
            _LTCGI_LightmapData_key = rkey.ToArray();
            _LTCGI_LightmapOffsets_val = rval.ToArray();
            _LTCGI_LightmapIndex_val = rival.ToArray();

            EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Resetting configuration", 0.9f);
            bakeInProgress = false;
            ResetConfiguration();

            /*Lightmapping.lightingDataAsset = prevLightmapData;
            LightmapSettings.lightmapsMode = prevLightmapMode;
            LightmapSettings.lightmaps = new LightmapData[prevLightmaps0.Length];
            for (int i = 0; i < LightmapSettings.lightmaps.Length; i++)
            {
                var data = new LightmapData();
                data.lightmapColor = prevLightmaps0[i];
                data.lightmapDir = prevLightmaps1[i];
                #pragma warning disable 0618
                data.lightmapLight = prevLightmaps2[i];
                #pragma warning restore 0618
                data.shadowMask = prevLightmaps3[i];
                LightmapSettings.lightmaps[i] = data;
            }*/

            EditorUtility.DisplayProgressBar("Finishing LTCGI bake", "Calling external event handlers", 1.0f);
            OnLTCGIShadowmapBakeComplete?.Invoke();

            EditorUtility.ClearProgressBar();
            LTCGI_Controller.Singleton.UpdateMaterials();

            Debug.Log("LTCGI: Shadowmap bake complete!");

            if (followupWithRealBake)
            {
                followupWithRealBake = false;

                #if BAKERY_INCLUDED
                if (followupBakery)
                {
                    EditorApplication.delayCall += () => {
                        var b = ftRenderLightmap.instance;
                        b.RenderButton(false);
                    };
                }
                else
                #endif
                {
                    EditorApplication.delayCall += () => {
                        Lightmapping.BakeAsync();
                    };
                }
            }
        }

        // includes ones on hidden/disabled objects
        private List<LTCGI_BakeReset> GetAllBakeResets()
        {
            List<LTCGI_BakeReset> found = new List<LTCGI_BakeReset>();
            foreach (LTCGI_BakeReset br in Resources.FindObjectsOfTypeAll(typeof(LTCGI_BakeReset)) as LTCGI_BakeReset[])
            {
                if (!EditorUtility.IsPersistent(br.gameObject.transform.root.gameObject) &&
                    !(br.gameObject.hideFlags == HideFlags.NotEditable || br.gameObject.hideFlags == HideFlags.HideAndDontSave))
                {
                    found.Add(br);
                }
            }
            return found;
        }

        [MenuItem("Tools/LTCGI/Force Settings Reset after Bake")]
        public static void ResetConfigurationMenu() => LTCGI_Controller.Singleton.ResetConfiguration();
        public void ResetConfiguration()
        {
            RenderSettings.ambientIntensity = previousAmbientIntensity;
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientSkyColor = previousAmbientColor;

            if (bakeMaterialReset_key != null)
            {
                for (int i = 0; i < bakeMaterialReset_key.Count; i++)
                {
                    bakeMaterialReset_key[i].globalIlluminationFlags = bakeMaterialReset_val[i];
                }
            }

            var resetters = GetAllBakeResets();
            foreach (LTCGI_BakeReset r in resetters)
            {
                if (r)
                {
                    r.ApplyReset();
                    DestroyImmediate(r);
                }
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkAllScenesDirty();
            EditorSceneManager.SaveOpenScenes();
        }
    }
    #endif
}