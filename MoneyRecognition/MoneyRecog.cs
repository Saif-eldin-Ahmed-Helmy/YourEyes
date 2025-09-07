using UnityEngine;
using Unity.Barracuda;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class MoneyDetector : MonoBehaviour
{
    public NNModel modelAsset;
    public RawImage rawImage;
    public Text totalText;
    [Range(0.1f, 2f)] public float maxProcessingTime = 0.5f;

    private Model runtimeModel;
    private IWorker worker;
    private WebCamTexture webcamTexture;
    private bool isProcessing;
    private Texture2D displayTexture;
    private RenderTexture resizeRT;

    private readonly string[] classLabels = { "5EGP", "10EGP", "20EGP", "50EGP", "100EGP", "200EGP" };
    private readonly int[] classValues = { 5, 10, 20, 50, 100, 200 };

    // cache time so it plays tts every 5 seconds
    private float lastTTSPlayTime = 0f;

    void Start()
    {
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);

        webcamTexture = new WebCamTexture(640, 480, 30);
        rawImage.texture = webcamTexture;
        webcamTexture.Play();

        displayTexture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        resizeRT = new RenderTexture(640, 640, 0);
    }

    void Update()
    {
        if (webcamTexture.didUpdateThisFrame)
        {
            displayTexture.SetPixels32(webcamTexture.GetPixels32());
            displayTexture.Apply();

            if (!isProcessing)
                StartCoroutine(ProcessFrame());
        }
    }

    IEnumerator ProcessFrame()
    {
        isProcessing = true;

        var tempTex = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGBA32, false);
        tempTex.SetPixels32(webcamTexture.GetPixels32());
        tempTex.Apply();

        Graphics.Blit(tempTex, resizeRT);
        using var input = new Tensor(resizeRT, channels: 3);
        var iterator = worker.StartManualSchedule(input);
        while (iterator.MoveNext()) ;

        using Tensor output = worker.PeekOutput();
        var detections = ApplyNMS(ProcessOutput(output), 0.3f);

        UpdateTotal(detections);

        Destroy(tempTex);
        isProcessing = false;
        yield return null;
    }

    List<Detection> ProcessOutput(Tensor output)
    {
        var detections = new List<Detection>();
        for (int i = 0; i < output.shape.width; i++)
        {
            float x = output[0, 0, i, 0];
            float y = output[0, 0, i, 1];
            float w = output[0, 0, i, 2];
            float h = output[0, 0, i, 3];

            float maxConf = 0;
            int clsIdx = -1;
            for (int c = 0; c < classValues.Length; c++)
            {
                float conf = output[0, 0, i, 4 + c];
                if (conf > maxConf)
                {
                    maxConf = conf;
                    clsIdx = c;
                }
            }

            if (maxConf < 0.1f) continue;

            detections.Add(new Detection
            {
                rect = new Rect(x - w / 2, y - h / 2, w, h),
                confidence = maxConf,
                classIndex = clsIdx
            });
        }
        return detections;
    }

    void UpdateTotal(List<Detection> detections)
    {
        // Count bills per class
        var counts = new Dictionary<int, int>();
        foreach (var d in detections)
        {
            if (!counts.ContainsKey(d.classIndex))
                counts[d.classIndex] = 0;
            counts[d.classIndex]++;
        }

        // Sum total
        int total = 0;
        foreach (var kv in counts)
            total += kv.Value * classValues[kv.Key];

        totalText.text = $"Total: {total} EGP";
        totalText.color = total > 0 ? Color.green : Color.red;

        if (Time.time - lastTTSPlayTime > 5f)
        {
            if (total > 0)
            {
                // Build Arabic TTS string
                var sb = new StringBuilder();
                // Total
                sb.Append($"المجموع {NumberToArabicWords(total)} جنيه. ");

                // Each bill type
                foreach (var kv in counts)
                {
                    int cls = kv.Key;
                    int cnt = kv.Value;
                    int val = classValues[cls];

                    sb.Append($"فيه {NumberToArabicWords(cnt)} {NumberToArabicWords(val)} جنيه. ");
                }

                TTSManager.Speak(sb.ToString().Trim());
                lastTTSPlayTime = Time.time;
            }
            else
            {
                TTSManager.Speak("لا يوجد نقود");
                lastTTSPlayTime = Time.time;
            }
        }
    }

    // ------- Arabic Number Converter -------
    private static readonly string[] Units = {
        "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة",
        "ستة", "سبعة", "ثمانية", "تسعة"
    };

    private static readonly Dictionary<int, string> Teens = new Dictionary<int, string> {
        {10, "عشرة"}, {11, "أحد عشر"}, {12, "اثنا عشر"},
        {13, "ثلاثة عشر"}, {14, "أربعة عشر"}, {15, "خمسة عشر"},
        {16, "ستة عشر"}, {17, "سبعة عشر"}, {18, "ثمانية عشر"},
        {19, "تسعة عشر"}
    };

    private static readonly Dictionary<int, string> Tens = new Dictionary<int, string> {
        {20, "عشرون"}, {30, "ثلاثون"}, {40, "أربعون"},
        {50, "خمسون"}, {60, "ستون"}, {70, "سبعون"},
        {80, "ثمانون"}, {90, "تسعون"}
    };

    private static readonly Dictionary<int, string> Hundreds = new Dictionary<int, string> {
        {100, "مائة"}, {200, "مائتان"}, {300, "ثلاثمائة"},
        {400, "أربعمائة"}, {500, "خمسمائة"}, {600, "ستمائة"},
        {700, "سبعمائة"}, {800, "ثمانمائة"}, {900, "تسعمائة"}
    };

    private static string NumberToArabicWords(int num)
    {
        if (num == 0) return "صفر";

        var parts = new List<string>();

        // Hundreds
        int h = (num / 100) * 100;
        if (h > 0 && Hundreds.TryGetValue(h, out var hWord))
        {
            parts.Add(hWord);
            num %= 100;
        }

        // Teens (10–19)
        if (num >= 10 && num < 20)
        {
            parts.Add(Teens[num]);
            num = 0;
        }
        else
        {
            // Tens
            int t = (num / 10) * 10;
            if (t > 0 && Tens.TryGetValue(t, out var tWord))
            {
                parts.Add(tWord);
                num %= 10;
            }
            // Units
            if (num > 0)
                parts.Add(Units[num]);
        }

        // Join with "و"
        return string.Join(" و", parts);
    }

    List<Detection> ApplyNMS(List<Detection> detections, float iouThreshold)
    {
        var results = new List<Detection>();
        detections.Sort((a, b) => b.confidence.CompareTo(a.confidence));
        while (detections.Count > 0)
        {
            var top = detections[0];
            results.Add(top);
            detections.RemoveAt(0);
            for (int i = detections.Count - 1; i >= 0; i--)
            {
                if (CalculateIoU(top.rect, detections[i].rect) > iouThreshold)
                    detections.RemoveAt(i);
            }
        }
        return results;
    }

    float CalculateIoU(Rect a, Rect b)
    {
        float interW = Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
        float interH = Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
        float inter = interW * interH;
        float union = a.width * a.height + b.width * b.height - inter;
        return inter / union;
    }

    void OnDestroy()
    {
        worker?.Dispose();
        webcamTexture?.Stop();
        Destroy(resizeRT);
        Destroy(displayTexture);
    }

    private struct Detection
    {
        public Rect rect;
        public float confidence;
        public int classIndex;
    }
}
