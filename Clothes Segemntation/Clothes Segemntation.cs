using UnityEngine;
using Unity.Barracuda;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public class ClothesSegmentation : MonoBehaviour
{
    [Header("Model & UI")]
    public NNModel modelAsset;
    public RawImage maskedImage;
    public Text resultText;
    public float confidenceThreshold = 0.5f;

    private Model runtimeModel;
    private IWorker worker;
    private WebCamTexture webcamTexture;
    private bool isProcessing;
    private RenderTexture modelInputRT;
    private Texture2D maskedTexture;

    // Clothing class definitions (align with your model)
    private readonly Dictionary<int, string> classNames = new Dictionary<int, string>
    {
        {0, "background"}, {1, "shirt"}, {2, "jacket"}, {3, "pants"},
        {4, "skirt"}, {5, "dress"}, {6, "jeans"}, {7, "t-shirt"},
        {8, "sweater"}, {9, "blouse"}, {10, "coat"}, {11, "shirt"},
        {12, "shorts"}, {13, "hoodie"}, {14, "cardigan"}, {15, "suit"},
        {16, "blazer"}, {17, "scarf"}, {18, "hat"}
    };
    private readonly int[] clothingIDs = { 2, 4, 6, 11, 14, 15 };

    void Start()
    {
        // Load the model and create a worker
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.Auto, runtimeModel);

        // Log available output tensor names
        foreach (var outputName in runtimeModel.outputs)
            Debug.Log($"[Seg] Model output tensor: {outputName}");

        // Initialize camera and textures
        webcamTexture = new WebCamTexture(640, 480, 30);
        webcamTexture.Play();
        modelInputRT = new RenderTexture(224, 224, 0);
        maskedTexture = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGBA32, false);
    }

    void Update()
    {
        if (!isProcessing && webcamTexture.didUpdateThisFrame)
            StartCoroutine(ProcessFrame());
    }

    IEnumerator ProcessFrame()
    {
        isProcessing = true;

        // Copy camera frame to the model input RenderTexture
        Graphics.Blit(webcamTexture, modelInputRT);

        // Prepare input tensor
        using (var inputTensor = new Tensor(modelInputRT, channels: 3))
        {
            // Run inference
            worker.Execute(inputTensor);
        }

        // Choose the correct output by name (replace index if needed)
        string segName = runtimeModel.outputs[0];
        Tensor logits = worker.PeekOutput(segName);

        Debug.Log($"[Seg] Using output '{segName}' with shape {logits.shape} and length {logits.length}");

        // Process segmentation and update UI
        Texture2D result = ProcessSegmentation(logits, webcamTexture);
        maskedImage.texture = result;

        logits.Dispose();
        isProcessing = false;
        yield return null;
    }

    private Texture2D ProcessSegmentation(Tensor logits, Texture source)
    {
        int batch = logits.shape.batch;
        int height = logits.shape.height;
        int width = logits.shape.width;
        int channels = logits.shape.channels;
        int numClasses = classNames.Count;

        Debug.Log($"[Seg] Tensor format: batch={batch}, height={height}, width={width}, channels={channels}");

        // Determine if output is NCHW (channels > numClasses)
        bool isNCHW = channels > numClasses;

        // Read source pixels
        Texture2D srcTex = ToTexture2D(source);
        Color[] pixels = srcTex.GetPixels();

        for (int y = 0; y < srcTex.height; y++)
        {
            for (int x = 0; x < srcTex.width; x++)
            {
                int mx = x * width / srcTex.width;
                int my = y * height / srcTex.height;

                float maxConf = 0f;
                int bestClass = 0;

                for (int c = 0; c < numClasses; c++)
                {
                    float conf = isNCHW ? logits[0, c, my, mx] : logits[0, my, mx, c];
                    if (conf > maxConf)
                    {
                        maxConf = conf;
                        bestClass = c;
                    }
                }

                // Mask out low confidence or non-target classes
                if (maxConf < confidenceThreshold || System.Array.IndexOf(clothingIDs, bestClass) < 0)
                {
                    pixels[y * srcTex.width + x] = Color.clear;
                }
            }
        }

        maskedTexture.SetPixels(pixels);
        maskedTexture.Apply();
        return maskedTexture;
    }

    private Texture2D ToTexture2D(Texture tex)
    {
        RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height);
        Graphics.Blit(tex, rt);
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        result.Apply();

        RenderTexture.ReleaseTemporary(rt);
        return result;
    }

    public void OnAnalyzeButtonClick()
    {
        StartCoroutine(SendToGeminiAndDisplay(maskedTexture));
    }

    private IEnumerator SendToGeminiAndDisplay(Texture2D maskedTex)
    {
        byte[] imageBytes = maskedTex.EncodeToJPG();

        yield return GeminiHelper.Instance.SendClothingAnalysisRequest(
            imageBytes,
            response =>
            {
                if (!string.IsNullOrEmpty(response.error))
                {
                    resultText.text = $"Error: {response.error}";
                    return;
                }
                resultText.text = $"Arabic Summary: {response.summary_egyptian_arabic}\nJSON Data: {JObject.FromObject(response)}";
            }
        );
    }

    void OnDestroy()
    {
        worker?.Dispose();
        webcamTexture?.Stop();
        Destroy(modelInputRT);
        Destroy(maskedTexture);
    }
}
