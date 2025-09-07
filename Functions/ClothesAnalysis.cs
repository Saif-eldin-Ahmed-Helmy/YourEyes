// ClothesAnalysis.cs
using UnityEngine;
using UnityEngine.Networking;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;

public class ClothesAnalysis : MonoBehaviour
{
    private static ClothesAnalysis _instance;
    public static ClothesAnalysis Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("ClothesAnalysis");
                _instance = go.AddComponent<ClothesAnalysis>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    WebCamTextureToMatHelper webCamHelper;
    bool isProcessing, isActive;
    public string apiUrl = "http://172.20.10.3:5000/clothes";

    [Serializable] class GeminiResponse { public Candidate[] candidates; }
    [Serializable] class Candidate { public Content content; }
    [Serializable] class Content { public Part[] parts; }
    [Serializable] class Part { public string text; }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
            Destroy(gameObject);
    }

    void Start()
    {
        webCamHelper = FindObjectOfType<WebCamTextureToMatHelper>();
    }

    public void ActivateMode()
    {
        isActive = true;
    }

    public void Deactivate()
    {
        isActive = false;
        isProcessing = false;
    }

    void Update()
    {
        if (isActive && webCamHelper.IsPlaying() && webCamHelper.DidUpdateThisFrame() && !isProcessing)
            StartCoroutine(ProcessFrame());
    }

    IEnumerator ProcessFrame()
    {
        isProcessing = true;
        var rgba = webCamHelper.GetMat();
        var snap = new Texture2D(rgba.cols(), rgba.rows(), TextureFormat.RGB24, false);
        Utils.matToTexture2D(rgba, snap);
        byte[] jpg = snap.EncodeToJPG(80);
        Destroy(snap);

        var form = new List<IMultipartFormSection> {
            new MultipartFormFileSection("image", jpg, "frame.jpg", "image/jpeg")
        };
        using var uwr = UnityWebRequest.Post(apiUrl, form);
        uwr.timeout = 10;
        yield return uwr.SendWebRequest();

        if (uwr.result == UnityWebRequest.Result.Success)
        {
            try
            {
                var gem = JsonUtility.FromJson<GeminiResponse>(uwr.downloadHandler.text);
                string txt = gem?.candidates?[0]?.content?.parts?[0]?.text?.Trim();
                if (string.IsNullOrEmpty(txt)) throw new Exception();
                if (txt.Equals("BAD", StringComparison.OrdinalIgnoreCase)) { isProcessing = false; yield break; }
                int s = txt.IndexOf('{'), e = txt.LastIndexOf('}');
                string json = (s >= 0 && e > s) ? txt.Substring(s, e - s + 1) : txt;
                var m = Regex.Match(json, "\"summary_egyptian_arabic\"\\s*:\\s*\"(.*?)\"");
                if (m.Success) TTSManager.Speak(m.Groups[1].Value);
                isActive = false;
            }
            catch { }
        }

        isProcessing = false;
    }
}
