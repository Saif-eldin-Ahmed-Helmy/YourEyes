using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils.Helper;
using OpenCVForUnity.DnnModel;
using OpenCVForUnity.UnityUtils;

/// <summary>
/// Singleton module for facial expression recognition with Arabic TTS feedback,
/// with a 5-second detection window.
/// </summary>
public class FacialExpression : MonoBehaviour
{
    private static FacialExpression _instance;
    public static FacialExpression Instance
    {
        get
        {
            if (_instance == null)
            {
                Debug.Log("[FacialExpression] Creating instance");
                var go = new GameObject("FacialExpressionModule");
                _instance = go.AddComponent<FacialExpression>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    WebCamTextureToMatHelper webCamHelper;
    YuNetV2FaceDetector faceDetector;
    FacialExpressionRecognizer facialExpressionRecognizer;

    bool isDetecting = false;

    protected static readonly string FACE_DETECTION_MODEL_FILENAME = "OpenCVForUnity/dnn/face_detection_yunet_2023mar.onnx";
    protected static readonly string FACIAL_EXPRESSION_RECOGNITION_MODEL_FILENAME = "OpenCVForUnity/dnn/facial_expression_recognition_mobilefacenet_2022july.onnx";
    protected static readonly string FACE_RECOGNITION_MODEL_FILENAME = "OpenCVForUnity/dnn/face_recognition_sface_2021dec.onnx";

    string faceDetectionModelPath;
    string expressionModelPath;
    string recognitionModelPath;

    int inputSizeW = 320;
    int inputSizeH = 320;
    float scoreThreshold = 0.9f;
    float nmsThreshold = 0.3f;
    int topK = 5000;

    void Awake()
    {
        Debug.Log("[FacialExpression] Awake");
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Debug.LogWarning("[FacialExpression] Duplicate instance found, destroying");
            Destroy(gameObject);
        }
    }

    void Start()
    {
        Debug.Log("[FacialExpression] Start: locating WebCamTextureToMatHelper");
        webCamHelper = FindObjectOfType<WebCamTextureToMatHelper>();
        if (webCamHelper == null)
            Debug.LogError("[FacialExpression] WebCamTextureToMatHelper not in scene");
        InitializeModels();
    }

    void InitializeModels()
    {
        Debug.Log("[FacialExpression] Initializing models");
        faceDetectionModelPath = Utils.getFilePath(FACE_DETECTION_MODEL_FILENAME);
        expressionModelPath = Utils.getFilePath(FACIAL_EXPRESSION_RECOGNITION_MODEL_FILENAME);
        recognitionModelPath = Utils.getFilePath(FACE_RECOGNITION_MODEL_FILENAME);

        if (string.IsNullOrEmpty(faceDetectionModelPath) || string.IsNullOrEmpty(expressionModelPath) || string.IsNullOrEmpty(recognitionModelPath))
        {
            Debug.LogError($"[FacialExpression] Invalid model paths: {faceDetectionModelPath}, {expressionModelPath}, {recognitionModelPath}");
            return;
        }

        faceDetector = new YuNetV2FaceDetector(faceDetectionModelPath, "", new Size(inputSizeW, inputSizeH), scoreThreshold, nmsThreshold, topK);
        facialExpressionRecognizer = new FacialExpressionRecognizer(expressionModelPath, recognitionModelPath, "");
        Debug.Log("[FacialExpression] Models loaded");
    }

    /// <summary>
    /// Begin a 5-second facial expression detection period.
    /// </summary>
    public void ActivateMode()
    {
        if (isDetecting)
        {
            Debug.LogWarning("[FacialExpression] Already detecting");
            return;
        }
        Debug.Log("[FacialExpression] ActivateMode: starting detection for 5s");
        isDetecting = true;
        StartCoroutine(DetectionRoutine());
    }

    IEnumerator DetectionRoutine()
    {
        float startTime = Time.time;
        bool spoke = false;

        Debug.Log("[FacialExpression] DetectionRoutine: waiting for frames");
        while (Time.time - startTime < 5f)
        {
            if (webCamHelper != null && webCamHelper.IsPlaying() && webCamHelper.DidUpdateThisFrame())
            {
                Mat rgba = webCamHelper.GetMat();
                if (rgba != null)
                {
                    Mat bgr = new Mat();
                    Imgproc.cvtColor(rgba, bgr, Imgproc.COLOR_RGBA2BGR);
                    Mat faces = faceDetector.infer(bgr);
                    if (faces != null && faces.rows() > 0)
                    {
                        Debug.Log($"[FacialExpression] Detected {faces.rows()} faces, inferring expression");
                        Mat expr = facialExpressionRecognizer.infer(bgr, faces.row(0));
                        if (expr != null && !expr.empty())
                        {
                            string label = GetArabicLabel(expr);
                            Debug.Log($"[FacialExpression] Expression detected: {label}");
                            if (!string.IsNullOrEmpty(label))
                            {
                                TTSManager.Speak(label);
                                spoke = true;
                            }
                        }
                        break;
                    }
                }
            }
            yield return null;
        }

        if (!spoke)
            Debug.Log("[FacialExpression] No expression detected within 5 seconds");

        isDetecting = false;
        Debug.Log("[FacialExpression] DetectionRoutine complete");
    }

    /// <summary>
    /// Maps the expression output scores to an Arabic label.
    /// </summary>
    string GetArabicLabel(Mat exprMat)
    {
        if (exprMat == null)
        {
            Debug.LogError("[FacialExpression] GetArabicLabel: exprMat is null");
            return null;
        }
        int count = exprMat.cols();
        float[] scores = new float[count];
        exprMat.get(0, 0, scores);

        int maxIdx = 0;
        for (int i = 1; i < count; i++)
            if (scores[i] > scores[maxIdx]) maxIdx = i;

        string[] arabic = { "غضب", "اشمئزاز", "خوف", "سعادة", "حزن", "دهشة", "محايد" };
        if (maxIdx < arabic.Length)
            return arabic[maxIdx];

        Debug.LogWarning($"[FacialExpression] Label index out of range: {maxIdx}");
        return null;
    }
}
