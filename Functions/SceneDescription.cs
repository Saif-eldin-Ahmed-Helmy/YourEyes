// SceneDescription.cs
using UnityEngine;
using UnityEngine.Networking;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Collections;
using System.Collections.Generic;
using System;

public class SceneDescription : MonoBehaviour
{
    private static SceneDescription _instance;
    public static SceneDescription Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("SceneDescription");
                _instance = go.AddComponent<SceneDescription>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    WebCamTextureToMatHelper webCamHelper;
    bool isProcessing, isActive;
    [Tooltip("Point this to your Flask server describe route")]
    public string apiUrl = "http://172.20.10.3:5000/describe";

    [Serializable]
    class DescribeResponse { public string caption; }

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
        if (webCamHelper == null)
            Debug.LogError("WebCamTextureToMatHelper not found in scene.");
    }

    /// <summary>
    /// Call to start sending frames for description with a 1-second delay
    /// </summary>
    public void ActivateMode()
    {
        // Notify user in Arabic and delay before capturing
        StartCoroutine(NotifyAndActivate());
    }

    private IEnumerator NotifyAndActivate()
    {
        TTSManager.Speak("جاري تحليل المشهد"); // "Analysing scene"
        yield return new WaitForSeconds(1f);
        isActive = true;
    }

    /// <summary>
    /// Stops sending frames
    /// </summary>
    public void Deactivate()
    {
        isActive = false;
        isProcessing = false;
    }

    void Update()
    {
        if (isActive && webCamHelper.IsPlaying() && webCamHelper.DidUpdateThisFrame() && !isProcessing)
        {
            StartCoroutine(ProcessFrame());
        }
    }

    IEnumerator ProcessFrame()
    {
        isProcessing = true;

        // grab current camera frame as Texture2D
        Mat rgbaMat = webCamHelper.GetMat();
        Texture2D snap = new Texture2D(rgbaMat.cols(), rgbaMat.rows(), TextureFormat.RGB24, false);
        Utils.matToTexture2D(rgbaMat, snap);
        byte[] jpg = snap.EncodeToJPG(80);
        Destroy(snap);

        // package as multipart form
        var form = new List<IMultipartFormSection> {
            new MultipartFormFileSection("image", jpg, "scene.jpg", "image/jpeg")
        };

        using var uwr = UnityWebRequest.Post(apiUrl, form);
        uwr.timeout = 20;
        yield return uwr.SendWebRequest();

        if (uwr.result == UnityWebRequest.Result.Success)
        {
            try
            {
                var resp = JsonUtility.FromJson<DescribeResponse>(uwr.downloadHandler.text);
                if (!string.IsNullOrEmpty(resp.caption))
                {
                    TTSManager.Speak(resp.caption.Trim());
                    // once you get a description, stop until reactivated
                    isActive = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to parse describe response: {e}");
            }
        }
        else
        {
            Debug.LogWarning($"Describe request error: {uwr.error}");
        }

        isProcessing = false;
    }
}
