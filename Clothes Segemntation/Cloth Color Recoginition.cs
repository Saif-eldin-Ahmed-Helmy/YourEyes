using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Text.RegularExpressions;


public class ClothesAnalyzer : MonoBehaviour
{

    [Serializable]
    public class GeminiResponse
    {
        public List<Candidate> candidates;
    }

    [Serializable]
    public class Candidate
    {
        public Content content;
    }

    [Serializable]
    public class Content
    {
        public List<Part> parts;
    }

    [Serializable]
    public class Part
    {
        public string text;
    }

    [Header("UI")]
    public RawImage rawImage;
    public Text errorText;

    [Header("API")]
    public string apiUrl = "http://172.20.10.3:5000/clothes";

    private WebCamTexture webcamTexture;
    private bool isProcessing = false;

    void Start()
    {
        webcamTexture = new WebCamTexture(640, 480, 30);
        rawImage.texture = webcamTexture;
        rawImage.material.mainTexture = webcamTexture;
        webcamTexture.Play();
    }

    void Update()
    {
        if (webcamTexture.didUpdateThisFrame && !isProcessing)
            StartCoroutine(ProcessFrame());
    }

    private IEnumerator ProcessFrame()
    {
        isProcessing = true;
        errorText.text = "";

        rawImage.texture = webcamTexture;

        Texture2D snap = new Texture2D(webcamTexture.width, webcamTexture.height, TextureFormat.RGB24, false);
        snap.SetPixels(webcamTexture.GetPixels());
        snap.Apply();
        byte[] jpg = snap.EncodeToJPG(80);
        Destroy(snap);

        var form = new List<IMultipartFormSection> {
        new MultipartFormFileSection("image", jpg, "frame.jpg", "image/jpeg")
    };

        using var uwr = UnityWebRequest.Post(apiUrl, form);
        uwr.timeout = 10;
        yield return uwr.SendWebRequest();

        if (uwr.result != UnityWebRequest.Result.Success)
        {
            errorText.text = $"API Error {uwr.responseCode}: {uwr.error}";
            Debug.LogError($"[ClothesAnalyzer] HTTP {uwr.responseCode}: {uwr.error}\n{uwr.downloadHandler.text}");
        }
        else
        {
            string responseText = uwr.downloadHandler.text;

            try
            {
                // ✅ Parse raw JSON directly
                GeminiResponse gemini = JsonUtility.FromJson<GeminiResponse>(responseText);

                // ✅ Safe null-checking
                string partText = gemini?.candidates?[0]?.content?.parts?[0]?.text?.Trim();
                if (string.IsNullOrEmpty(partText))
                    throw new Exception("No text found in response");
                Debug.Log($"[ClothesAnalyzer] Part Text: {partText}");

                if (partText.Equals("BAD", StringComparison.OrdinalIgnoreCase))
                {
                    TTSManager.Speak("حاول مجددا");
                    isProcessing = false;
                    yield break;
                }
                else
                {
                    Debug.Log($"[ClothesAnalyzer] Text: {partText}");
                }

                // ✅ Step 3: Extract and clean JSON from `text`
                string cleaned = ExtractJsonFromCodeBlock(partText);
                string summaryArabic = ExtractSummaryArabic(cleaned);

                if (string.IsNullOrEmpty(summaryArabic))
                    throw new Exception("summary_egyptian_arabic not found");

                TTSManager.Speak(summaryArabic);
            }
            catch (Exception e)
            {
                errorText.text = "Parse Error: " + e.Message;
                Debug.LogError($"[ClothesAnalyzer] JSON parse failed:\n{responseText}\n{e}");
            }
        }

        yield return null;
        isProcessing = false;
    }

    // Unity's JsonUtility doesn't handle top-level arrays well
    private string FixJson(string json)
    {
        return json.Replace("\"parts\": [", "\"parts\": [{").Replace("}]", "}]");
    }

    private string ExtractJsonFromCodeBlock(string text)
    {
        if (text.StartsWith("```json"))
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start == -1 || end == -1 || end <= start)
                throw new Exception("Could not parse JSON code block.");
            return text.Substring(start, end - start + 1);
        }
        return text;
    }

    private string ExtractSummaryArabic(string json)
    {
        Match match = Regex.Match(json, "\"summary_egyptian_arabic\"\\s*:\\s*\"(.*?)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    void OnDestroy()
    {
        if (webcamTexture != null)
        {
            webcamTexture.Stop();
            Destroy(webcamTexture);
        }
    }
}
