﻿using System;
using UnityEngine;

#if UDONSHARP
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
#endif

#if COMPILER_UDONSHARP
using GlobalShader = VRC.SDKBase.VRCShader;
#else
using GlobalShader = UnityEngine.Shader;
#endif

#if UDONSHARP
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class LTCGI_UdonAdapter : UdonSharpBehaviour
#else
public class LTCGI_UdonAdapter : MonoBehaviour
#endif
{
    [Header("Internal Data (auto-generated, do not edit!)")]
    public Renderer[] _Renderers;
    public Texture2D _LTCGI_DefaultLightmap;
    public Texture2D[] _LTCGI_Lightmaps;
    public Vector4[] _LTCGI_LightmapST;
    public float[] _LTCGI_Mask;
    public float[] _LTCGI_MaskAvatars;
    public Vector4 _LTCGI_LightmapMult;
    public GameObject[] _Screens;
    public Texture2D _LTCGI_lut1, _LTCGI_lut2;
    public Texture[] _LTCGI_LODs;
    public Texture2DArray _LTCGI_Static_LODs_0;
    public Texture2DArray _LTCGI_Static_LODs_1;
    public Texture2DArray _LTCGI_Static_LODs_2;
    public Texture2DArray _LTCGI_Static_LODs_3;
    public Vector4[] _LTCGI_Vertices_0, _LTCGI_Vertices_1, _LTCGI_Vertices_2, _LTCGI_Vertices_3;
    private Vector4[] _LTCGI_Vertices_0t, _LTCGI_Vertices_1t, _LTCGI_Vertices_2t, _LTCGI_Vertices_3t;
    public Vector4[] _LTCGI_ExtraData;
    public Texture2D _LTCGI_static_uniforms;
    public Transform[] _LTCGI_ScreenTransforms;
    public int _LTCGI_ScreenCount;
    public int _LTCGI_ScreenCountMaskedAvatars;
    public int[] _LTCGI_ScreenCountMasked;
    public int _LTCGI_ScreenCountDynamic;
    public CustomRenderTexture BlurCRTInput;

    private int prop_Udon_LTCGI_ExtraData;
    private int prop_Udon_LTCGI_Vertices_0;
    private int prop_Udon_LTCGI_Vertices_1;
    private int prop_Udon_LTCGI_Vertices_2;
    private int prop_Udon_LTCGI_Vertices_3;

    private readonly Vector4 defaultLMST = new Vector4(1, 1, 0, 0);

    void Start()
    {
        Debug.Log("LTCGI adapter start");

        if (_LTCGI_ScreenCount == 0)
        {
            Debug.LogError("LTCGI Adapter: No screens found! Try deleting the LTCGI_UdonAdapter component from the controller object and clicking 'Force Update' on the controller if this is unexpected.");
            this.enabled = false;
            return;
        }

        var stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();
        _Initialize();
        stopwatch.Stop();

        var udon = false;
#if UDONSHARP
        udon = true;
#endif
        Debug.Log($"LTCGI adapter started for {_LTCGI_ScreenCount} ({_LTCGI_ScreenCountDynamic} dynamic) screens (max: {_LTCGI_Vertices_0t.Length}), {_Renderers.Length} renderers, GlobalShader mode, udon: {udon}, took: {stopwatch.ElapsedMilliseconds}ms");

        if (_LTCGI_ScreenCountDynamic == 0 || _Renderers.Length == 0)
        {
            Debug.Log("LTCGI adapter going to sleep 😴");
            this.enabled = false;
        }
    }

    public void _Initialize()
    {
        // must be full length (16) otherwise Unity will allocate too little GPU memory and this may break between worlds that use different amounts of screens
        // pretty cursed, but w/e, keep in mind that this means vrc worlds that adjust the max cap above 16 will most likely not work in-game
        var maxScreens = _LTCGI_Vertices_0.Length;
        _LTCGI_Vertices_0t = new Vector4[maxScreens];
        _LTCGI_Vertices_1t = new Vector4[maxScreens];
        _LTCGI_Vertices_2t = new Vector4[maxScreens];
        _LTCGI_Vertices_3t = new Vector4[maxScreens];

        for (int i = 0; i < _LTCGI_ScreenCount; i++)
        {
            var transform = _LTCGI_ScreenTransforms[i];
            _LTCGI_Vertices_0t[i] = CalcTransform(_LTCGI_Vertices_0[i], transform);
            _LTCGI_Vertices_1t[i] = CalcTransform(_LTCGI_Vertices_1[i], transform);
            _LTCGI_Vertices_2t[i] = CalcTransform(_LTCGI_Vertices_2[i], transform);
            _LTCGI_Vertices_3t[i] = CalcTransform(_LTCGI_Vertices_3[i], transform);
        }

        // Set global material properties (anything not overridden below is for avatar support)
        for (int j = 0; j < _LTCGI_LODs.Length; j++)
            if (_LTCGI_LODs[j] != null)
                GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Texture_LOD" + j), _LTCGI_LODs[j]);

        if (_LTCGI_Static_LODs_0 != null)
            GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Texture_LOD0_arr"), _LTCGI_Static_LODs_0);
        if (_LTCGI_Static_LODs_1 != null)
            GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Texture_LOD1_arr"), _LTCGI_Static_LODs_1);
        if (_LTCGI_Static_LODs_2 != null)
            GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Texture_LOD2_arr"), _LTCGI_Static_LODs_2);
        if (_LTCGI_Static_LODs_3 != null)
            GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Texture_LOD3_arr"), _LTCGI_Static_LODs_3);

        GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_lut1"), _LTCGI_lut1);
        GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_lut2"), _LTCGI_lut2);

        GlobalShader.SetGlobalFloatArray(GlobalShader.PropertyToID("_Udon_LTCGI_Mask"), _LTCGI_MaskAvatars);
        #if COMPILER_UDONSHARP
        GlobalShader.SetGlobalInteger(GlobalShader.PropertyToID("_Udon_LTCGI_ScreenCount"), _LTCGI_ScreenCountMaskedAvatars);
        #else
        GlobalShader.SetGlobalInt(GlobalShader.PropertyToID("_Udon_LTCGI_ScreenCount"), _LTCGI_ScreenCountMaskedAvatars);
        #endif

        _SetGlobalState(true);

        if (_LTCGI_static_uniforms != null)
            GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_static_uniforms"), _LTCGI_static_uniforms);
        if (_LTCGI_DefaultLightmap != null)
            GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Lightmap"), _LTCGI_DefaultLightmap);

        // Set per world-renderer overrides
        var maskSubset = new float[_LTCGI_ScreenCount];
        for (int i = 0; i < _Renderers.Length; i++)
        {
            var r = _Renderers[i];
            var block = new MaterialPropertyBlock();
            if (r.HasPropertyBlock())
                r.GetPropertyBlock(block);

            Array.Copy(_LTCGI_Mask, i * _LTCGI_ScreenCount, maskSubset, 0, _LTCGI_ScreenCount);
            block.SetFloatArray("_Udon_LTCGI_Mask", maskSubset);
            block.SetInt("_Udon_LTCGI_ScreenCount", _LTCGI_ScreenCountMasked[i]);

            if (_LTCGI_Lightmaps[i] != null)
                block.SetTexture("_Udon_LTCGI_Lightmap", _LTCGI_Lightmaps[i]);
            block.SetVector("_Udon_LTCGI_LightmapMult", _LTCGI_LightmapMult);

            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name.StartsWith("Combined Mesh"))
            {
                block.SetVector("_Udon_LTCGI_LightmapST", defaultLMST);
            }
            else
            {
                var lmst = _LTCGI_LightmapST[i];
                block.SetVector("_Udon_LTCGI_LightmapST", lmst);
            }

            r.SetPropertyBlock(block);
        }

        prop_Udon_LTCGI_ExtraData = GlobalShader.PropertyToID("_Udon_LTCGI_ExtraData");
        prop_Udon_LTCGI_Vertices_0 = GlobalShader.PropertyToID("_Udon_LTCGI_Vertices_0");
        prop_Udon_LTCGI_Vertices_1 = GlobalShader.PropertyToID("_Udon_LTCGI_Vertices_1");
        prop_Udon_LTCGI_Vertices_2 = GlobalShader.PropertyToID("_Udon_LTCGI_Vertices_2");
        prop_Udon_LTCGI_Vertices_3 = GlobalShader.PropertyToID("_Udon_LTCGI_Vertices_3");

        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_ExtraData, _LTCGI_ExtraData);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_0, _LTCGI_Vertices_0t);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_1, _LTCGI_Vertices_1t);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_2, _LTCGI_Vertices_2t);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_3, _LTCGI_Vertices_3t);
    }

    private Vector4 CalcTransform(Vector4 i, Transform t)
    {
        var ret = (Vector4)t.TransformPoint((Vector3)i);
        ret.w = i.w; // keep UV the same
        return ret;
    }

    void Update()
    {
        // update vertex data
        for (int i = 0; i < _LTCGI_ScreenCountDynamic /* only run for dynamic screens */; i++)
        {
            var transform = _LTCGI_ScreenTransforms[i];
            _LTCGI_Vertices_0t[i] = CalcTransform(_LTCGI_Vertices_0[i], transform);
            _LTCGI_Vertices_1t[i] = CalcTransform(_LTCGI_Vertices_1[i], transform);
            _LTCGI_Vertices_2t[i] = CalcTransform(_LTCGI_Vertices_2[i], transform);
            _LTCGI_Vertices_3t[i] = CalcTransform(_LTCGI_Vertices_3[i], transform);
        }

        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_ExtraData, _LTCGI_ExtraData);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_0, _LTCGI_Vertices_0t);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_1, _LTCGI_Vertices_1t);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_2, _LTCGI_Vertices_2t);
        GlobalShader.SetGlobalVectorArray(prop_Udon_LTCGI_Vertices_3, _LTCGI_Vertices_3t);
    }

    // See the docs for more info:
    // https://github.com/PiMaker/ltcgi/wiki#udonsharp-api

    public int _GetIndex(GameObject screen)
    {
        var idx = Array.IndexOf(_Screens, screen);
        if (idx != -1)
        {
            // if (idx >= _LTCGI_ScreenCountDynamic)
            // {
            //     Debug.LogError("LTCGI: Cannot index non-dynamic object " + screen.name);
            //     return -1;
            // }

            return idx;
        }
        else
        {
            Debug.LogError("LTCGI: Cannot index unregistered object " + (screen == null ? "<null>" : screen.name));
            return -1;
        }
    }

    public Color _GetColor(int screen)
    {
        if (screen < 0) return Color.black;
        var data = _LTCGI_ExtraData[screen];
        return new Color(data.x, data.y, data.z);
    }

    public void _SetColor(int screen, Color color)
    {
        if (screen < 0) return;
        _LTCGI_ExtraData[screen].x = color.r;
        _LTCGI_ExtraData[screen].y = color.g;
        _LTCGI_ExtraData[screen].z = color.b;

        if (!this.enabled) Update();
    }

    public void _SetALBand(int screen, int band)
    {
        if (screen < 0 || band < 0 || band > 3) return;

        uint flags = getFlags(screen);

        // Clear flags
        flags &= ~(0b11u << 13);

        // Set new flags
        flags |= ((uint)band & 0b11u) << 13;

        setFlags(screen, flags);

        if (!this.enabled) Update();
    }

    public int _GetALBand(int screen)
    {
        if (screen < 0) return -1;

        uint flags = getFlags(screen);
        int band = (int)((flags >> 13) & 0b11u);

        return band;
    }

    public void _SetVideoTexture(Texture texture)
    {
        BlurCRTInput.material.SetTexture("_MainTex", texture);
        GlobalShader.SetGlobalTexture(GlobalShader.PropertyToID("_Udon_LTCGI_Texture_LOD0"), texture);
    }

    private uint getFlags(int screen)
    {
        var raw = _LTCGI_ExtraData[screen].w;
        return (uint)BitConverter.SingleToInt32Bits(raw);
    }

    private void setFlags(int screen, uint flags)
    {
        var converted = BitConverter.Int32BitsToSingle((int)flags);
        _LTCGI_ExtraData[screen].w = converted;
    }

    public void _SetTexture(int screen, uint index)
    {
        if (screen < 0) return;
        var flags = getFlags(screen);
        flags &= ~(0xfU << 4);
        flags |= (index & 0xf) << 4;
        setFlags(screen, flags);

        if (!this.enabled) Update();
    }

    private bool _globalState = false;
    public void _SetGlobalState(bool enabled)
    {
        float fstate = enabled ? 1.0f : 0.0f;
        GlobalShader.SetGlobalFloat(GlobalShader.PropertyToID("_Udon_LTCGI_GlobalEnable"), fstate);
        _globalState = enabled;
    }
    public bool _GetGlobalState() => _globalState;

    // extremely cursed compat stuff
    #if !UDONSHARP
    public void UpdateProxy() {}
    public void ApplyProxyModifications() {}
    #endif
}
