#if !UNITY_WSA_10_0

using UnityEngine;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.TextModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System;

public class TextTranslation : MonoBehaviour
{
    private static TextTranslation _instance;
    public static TextTranslation Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("TextTranslation");
                _instance = go.AddComponent<TextTranslation>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    WebCamTextureToMatHelper webCamHelper;
    bool isProcessing = false;
    bool isActive = false;
    bool isReady = false;
    float analysisStartTime;
    public float analysisDuration = 5f;

    List<string> results = new List<string>();
    Dictionary<string, int> freq = new Dictionary<string, int>();

    ERFilter erFilter1;
    ERFilter erFilter2;
    OCRHMMDecoder decoder;

    string nm1Path;
    string nm2Path;
    string transPath;
    string knnPath;

    void Start()
    {
        Utils.setDebugMode(true);
        webCamHelper = FindObjectOfType<WebCamTextureToMatHelper>();

        nm1Path = Utils.getFilePath("OpenCVForUnity/text/trained_classifierNM1.xml");
        nm2Path = Utils.getFilePath("OpenCVForUnity/text/trained_classifierNM2.xml");
        transPath = Utils.getFilePath("OpenCVForUnity/text/OCRHMM_transitions_table.xml");
#if UNITY_ANDROID && !UNITY_EDITOR
        knnPath = Utils.getFilePath("OpenCVForUnity/text/OCRHMM_knn_model_data.xml");
#else
        knnPath = Utils.getFilePath("OpenCVForUnity/text/OCRHMM_knn_model_data.xml.gz");
#endif

        erFilter1 = Text.createERFilterNM1(nm1Path, 8, 0.00015f, 0.13f, 0.2f, true, 0.1f);
        erFilter2 = Text.createERFilterNM2(nm2Path, 0.5f);

        double[] transData = GetTransitionProbabilitiesData(transPath);
        Mat transition = new Mat(62, 62, CvType.CV_64FC1);
        transition.put(0, 0, transData);
        Mat emission = Mat.eye(62, 62, CvType.CV_64FC1);
        string vocab = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        decoder = OCRHMMDecoder.create(knnPath, vocab, transition, emission);

        isReady = true;
        Debug.Log("TextTranslation is ready");
    }

    public void ActivateMode()
    {
        Debug.Log("Activating TextTranslation mode");
        if (!isReady)
        {
            Debug.Log("TextTranslation not ready yet, retrying in 1s");
            Invoke("ActivateMode", 1f);
            return;
        }
        isActive = true;
        analysisStartTime = Time.time;
        results.Clear();
        freq.Clear();
        Debug.Log("جارٍ تحليل النص");
        TTSManager.Speak("جارٍ تحليل النص");
    }

    void Update()
    {
        if (!isActive) return;

        if (Time.time < analysisStartTime + analysisDuration)
        {
            if (webCamHelper.IsPlaying() && webCamHelper.DidUpdateThisFrame() && !isProcessing)
                StartCoroutine(ProcessFrame());
        }
        else
        {
            FinishAnalysis();
        }
    }

    IEnumerator ProcessFrame()
    {
        isProcessing = true;
        Mat rgbaMat = webCamHelper.GetMat();
        string output = DetectText(rgbaMat);
        if (!string.IsNullOrEmpty(output))
        {
            Debug.Log("Detected text: " + output);
            results.Add(output);
            freq[output] = freq.ContainsKey(output) ? freq[output] + 1 : 1;
        }
        isProcessing = false;
        yield return null;
    }

    string DetectText(Mat rgbaFrame)
    {
        Debug.Log($"Input frame dimensions: {rgbaFrame.width()}x{rgbaFrame.height()}");

        // Convert RGBA to RGB
        Mat frame = new Mat();
        Imgproc.cvtColor(rgbaFrame, frame, Imgproc.COLOR_RGBA2RGB);

        // Grayscale
        Mat gray = new Mat();
        Imgproc.cvtColor(frame, gray, Imgproc.COLOR_RGB2GRAY);

        // Blur to reduce noise
        Mat blurred = new Mat();
        Imgproc.GaussianBlur(gray, blurred, new Size(5, 5), 0); // Increased blur kernel

        // Enhance contrast with CLAHE
        Mat enhanced = new Mat();
        Imgproc.equalizeHist(blurred, enhanced);
        blurred.Dispose();

        // Binarize with Otsu
        Mat binary = new Mat();
        Imgproc.threshold(enhanced, binary, 0, 255, Imgproc.THRESH_BINARY | Imgproc.THRESH_OTSU);
        enhanced.Dispose();

        // Morphological close to connect components
        Mat kernel = Imgproc.getStructuringElement(Imgproc.MORPH_RECT, new Size(5, 1)); // Better for text lines
        Imgproc.morphologyEx(binary, binary, Imgproc.MORPH_CLOSE, kernel);
        kernel.Dispose();

        // Invert for mask
        Mat mask = new Mat();
        Core.absdiff(binary, new Scalar(255), mask);

        // Detect extremal regions
        List<MatOfPoint> regions = new List<MatOfPoint>();
        try
        {
            Text.detectRegions(binary, erFilter1, erFilter2, regions);
            Debug.Log($"TextTranslation: detectRegions -> {regions.Count} regions");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Region detection error: {e.Message}");
        }

        // Group into text lines
        MatOfRect groupRects = new MatOfRect();
        try
        {
            Text.erGrouping(frame, binary, regions, groupRects);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Grouping error: {e.Message}");
        }

        List<OpenCVForUnity.CoreModule.Rect> rects = groupRects.toList();
        Debug.Log($"TextTranslation: erGrouping -> {rects.Count} rects");

        // Dispose regions and grouping
        foreach (var r in regions) r.Dispose();
        groupRects.Dispose();

        // OCR and choose longest
        string best = string.Empty;
        int maskWidth = mask.width(), maskHeight = mask.height();

        foreach (var r in rects)
        {
            // Debug rectangle info
            Debug.Log($"Processing rect: x={r.x}, y={r.y}, width={r.width}, height={r.height}");

            // Validate rectangle is within image bounds
            OpenCVForUnity.CoreModule.Rect safeRect = new OpenCVForUnity.CoreModule.Rect(
                Math.Max(0, r.x),
                Math.Max(0, r.y),
                Math.Min(r.width, maskWidth - Math.Max(0, r.x)),
                Math.Min(r.height, maskHeight - Math.Max(0, r.y))
            );

            // Skip if rectangle became too small
            if (safeRect.width < 5 || safeRect.height < 5)
            {
                Debug.Log($"Skipping rect (too small after bounds check): {safeRect.width}x{safeRect.height}");
                continue;
            }

            try
            {
                Mat roi = new Mat(mask, safeRect);

                // Add padding
                Mat paddedRoi = new Mat();
                Core.copyMakeBorder(roi, paddedRoi, 15, 15, 15, 15, Core.BORDER_CONSTANT, new Scalar(0));
                roi.Dispose();

                // OCR
                string txt = decoder.run(paddedRoi, 0);
                Debug.Log($"OCR result: '{txt}'");

                if (!string.IsNullOrEmpty(txt) && txt.Length > best.Length)
                    best = txt;

                paddedRoi.Dispose();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error processing ROI: {e.Message}");
            }
        }

        // Cleanup
        frame.Dispose();
        gray.Dispose();
        binary.Dispose();
        mask.Dispose();

        return best;
    }

    void FinishAnalysis()
    {
        isActive = false;
        if (results.Count > 0)
        {
            string best = GetBestResult();
            Debug.Log("Best result: " + best);
            TTSManager.Speak(best);
        }
        else
        {
            Debug.Log("No text detected after analysis");
            TTSManager.Speak("لم يتم التعرف على نص");
        }
    }

    string GetBestResult()
    {
        int max = 0; string best = string.Empty;
        foreach (var kv in freq)
            if (kv.Value > max) { max = kv.Value; best = kv.Key; }
        return best;
    }

    double[] GetTransitionProbabilitiesData(string filePath)
    {
        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.Load(filePath);
        XmlNode dataNode = xmlDoc.GetElementsByTagName("data").Item(0);
        string[] tokens = dataNode.InnerText.Split(new[] { ' ', '\r', '\n', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        double[] data = new double[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
            if (!double.TryParse(tokens[i], out data[i]))
                Debug.LogWarning($"Failed to parse '{tokens[i]}'");
        return data;
    }
}

#endif
