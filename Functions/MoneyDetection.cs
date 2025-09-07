// MoneyDetection.cs
using UnityEngine;
using Unity.Barracuda;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

public class MoneyDetection : MonoBehaviour
{
    private static MoneyDetection _instance;
    public static MoneyDetection Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("MoneyDetection");
                _instance = go.AddComponent<MoneyDetection>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    WebCamTextureToMatHelper webCamHelper;
    IWorker worker;
    RenderTexture resizeRT;
    Texture2D displayTexture;
    bool isProcessing, isActive;
    List<FrameResult> collectedFrames;
    readonly int[] classValues = { 5, 10, 20, 50, 100, 200 };
    float duration = 3f;

    class FrameResult
    {
        public Dictionary<int, int> counts;
        public Dictionary<int, float> avgConf;
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        webCamHelper = FindObjectOfType<WebCamTextureToMatHelper>();
        var modelAsset = Resources.Load<NNModel>("Models/money_recog");
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, ModelLoader.Load(modelAsset));
        displayTexture = new Texture2D(640, 480, TextureFormat.RGBA32, false);
        resizeRT = new RenderTexture(640, 640, 0);
    }

    public void ActivateMode()
    {
        collectedFrames = new List<FrameResult>();
        isActive = true;
        StartCoroutine(ModeCoroutine(duration));
    }

    public void Deactivate()
    {
        isActive = false;
    }

    IEnumerator ModeCoroutine(float duration)
    {
        float start = Time.time;
        while (Time.time - start < duration)
            yield return null;

        if (collectedFrames.Count == 0)
        {
            TTSManager.Speak("لا يوجد نقود");
            isActive = false;
            yield break;
        }

        // Group frames by their counts signature
        var groups = collectedFrames
            .GroupBy(fr => CountsKey(fr.counts))
            .OrderByDescending(g => g.Count());

        var bestGroup = groups.First();
        var representative = bestGroup.First();

        // compute total for this representative
        int total = representative.counts.Sum(kv => kv.Value * classValues[kv.Key]);

        // build TTS string
        var sb = new StringBuilder();
        sb.Append($"المجموع {NumberToArabicWords(total)} جنيه.");

        foreach (var kv in representative.counts.OrderBy(kv => classValues[kv.Key]))
        {
            int idx = kv.Key;
            int cnt = kv.Value;
            int val = classValues[idx];
            int confPercent = Mathf.RoundToInt(representative.avgConf[idx] * 100);
            sb.Append($"فيه {NumberToArabicWords(cnt)} {NumberToArabicWords(val)}");
        }

        TTSManager.Speak(sb.ToString().Trim());
        isActive = false;
    }

    void Update()
    {
        if (isActive &&
            webCamHelper.IsPlaying() &&
            webCamHelper.DidUpdateThisFrame() &&
            !isProcessing)
        {
            StartCoroutine(ProcessFrame());
        }
    }

    IEnumerator ProcessFrame()
    {
        isProcessing = true;

        Mat rgba = webCamHelper.GetMat();
        Utils.matToTexture2D(rgba, displayTexture);
        Graphics.Blit(displayTexture, resizeRT);

        using var input = new Tensor(resizeRT, 3);
        var it = worker.StartManualSchedule(input);
        while (it.MoveNext()) ;
        using var output = worker.PeekOutput();

        var dets = ApplyNMS(ProcessOutput(output), 0.3f);

        // per-frame counts & avg confidences
        var counts = new Dictionary<int, int>();
        var confSums = new Dictionary<int, float>();
        foreach (var d in dets)
        {
            if (!counts.ContainsKey(d.classIndex))
            {
                counts[d.classIndex] = 0;
                confSums[d.classIndex] = 0f;
            }
            counts[d.classIndex]++;
            confSums[d.classIndex] += d.confidence;
        }

        if (counts.Count > 0)
        {
            var avgConf = new Dictionary<int, float>();
            foreach (var kv in counts)
                avgConf[kv.Key] = confSums[kv.Key] / kv.Value;

            collectedFrames.Add(new FrameResult { counts = counts, avgConf = avgConf });
        }

        isProcessing = false;
        yield return null;
    }

    List<Detection> ProcessOutput(Tensor o)
    {
        var list = new List<Detection>();
        for (int i = 0; i < o.shape.width; i++)
        {
            float x = o[0, 0, i, 0],
                  y = o[0, 0, i, 1],
                  w = o[0, 0, i, 2],
                  h = o[0, 0, i, 3];
            float maxC = 0;
            int cls = -1;
            for (int c = 0; c < classValues.Length; c++)
            {
                float conf = o[0, 0, i, 4 + c];
                if (conf > maxC)
                {
                    maxC = conf;
                    cls = c;
                }
            }
            if (maxC < 0.1f) continue;
            list.Add(new Detection
            {
                rect = new UnityEngine.Rect(x - w / 2, y - h / 2, w, h),
                confidence = maxC,
                classIndex = cls
            });
        }
        return list;
    }

    List<Detection> ApplyNMS(List<Detection> dets, float th)
    {
        var res = new List<Detection>();
        dets.Sort((a, b) => b.confidence.CompareTo(a.confidence));
        while (dets.Count > 0)
        {
            var top = dets[0];
            res.Add(top);
            dets.RemoveAt(0);
            for (int i = dets.Count - 1; i >= 0; i--)
                if (CalculateIoU(top.rect, dets[i].rect) > th)
                    dets.RemoveAt(i);
        }
        return res;
    }

    float CalculateIoU(UnityEngine.Rect a, UnityEngine.Rect b)
    {
        float w = Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin));
        float h = Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin));
        float inter = w * h;
        float uni = a.width * a.height + b.width * b.height - inter;
        return inter / uni;
    }

    string CountsKey(Dictionary<int, int> counts)
    {
        var parts = counts.OrderBy(kv => kv.Key)
                          .Select(kv => $"{kv.Key}:{kv.Value}");
        return string.Join(";", parts);
    }

    struct Detection
    {
        public UnityEngine.Rect rect;
        public float confidence;
        public int classIndex;
    }

    // ------- Arabic Number Converter (supports thousands) -------
    static readonly string[] Units = { "", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة" };
    static readonly Dictionary<int, string> Teens = new Dictionary<int, string> {
        {10,"عشرة"},{11,"أحد عشر"},{12,"اثنا عشر"},{13,"ثلاثة عشر"},{14,"أربعة عشر"},
        {15,"خمسة عشر"},{16,"ستة عشر"},{17,"سبعة عشر"},{18,"ثمانية عشر"},{19,"تسعة عشر"} };
    static readonly Dictionary<int, string> Tens = new Dictionary<int, string> {
        {20,"عشرون"},{30,"ثلاثون"},{40,"أربعون"},{50,"خمسون"},{60,"ستون"},
        {70,"سبعون"},{80,"ثمانون"},{90,"تسعون"} };
    static readonly Dictionary<int, string> Hundreds = new Dictionary<int, string> {
        {100,"مائة"},{200,"مائتان"},{300,"ثلاثمائة"},{400,"أربعمائة"},{500,"خمسمائة"},
        {600,"ستمائة"},{700,"سبعمائة"},{800,"ثمانمائة"},{900,"تسعمائة"} };

    private static string NumberToArabicWords(int num)
    {
        if (num == 0) return "صفر";

        var parts = new List<string>();

        if (num >= 1000)
        {
            int thousands = num / 1000;
            parts.Add($"{NumberToArabicWords(thousands)} ألف");
            num %= 1000;
        }

        int h = (num / 100) * 100;
        if (h > 0 && Hundreds.TryGetValue(h, out var hw))
        {
            parts.Add(hw);
            num %= 100;
        }

        if (num >= 10 && num < 20)
        {
            parts.Add(Teens[num]);
            num = 0;
        }
        else
        {
            int t = (num / 10) * 10;
            if (t > 0 && Tens.TryGetValue(t, out var tw))
            {
                parts.Add(tw);
                num %= 10;
            }
            if (num > 0)
            {
                parts.Add(Units[num]);
            }
        }

        return string.Join(" و", parts);
    }
}
