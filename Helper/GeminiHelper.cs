using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

[System.Serializable]
public class ClothingResponseData
{
    public Dictionary<string, PersonData> persons;
    public Dictionary<string, string> other_clothing;
    public string summary_egyptian_arabic;
    public string error;
}

[System.Serializable]
public class PersonData
{
    public Dictionary<string, string> clothing;
    public bool glasses;
    public string hair_color;
}

public class GeminiHelper : MonoBehaviour
{
    private static GeminiHelper _instance;
    public static GeminiHelper Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("GeminiHelper");
                _instance = go.AddComponent<GeminiHelper>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    private const string API_KEY = "AIzaSyDPqsG_AFgOBtywBpwu8qWt-eB8z8mVQhk";
    private const string BASE_API_URL = "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=";

    // Clothing analysis specific prompt
    private const string CLOTHING_PROMPT = @"Analyze the image to identify all visible clothing items. For each item, determine its type and color.

If the image contains one or more individuals, please also identify:
- For each person, whether they are wearing glasses (true/false).
- For each person, their visible hair color.

Structure your response as a JSON object.

- If one or more people are present, create a top-level key for each person (e.g., ""person1"", ""person2""). The value for each person key should be another JSON object containing the keys ""clothing"" (with a value being a JSON object of ""clothing_type"": ""color"" pairs), ""glasses"" (true/false), and ""hair_color"" (""color"").
- If there are clothing items visible that are not being worn by any person, create a top-level key named ""other_clothing"". The value for this key should be a JSON object of ""clothing_type"": ""color"" pairs.
- Include a top-level key named ""summary_egyptian_arabic"" with a short, descriptive summary of the scene in Egyptian Arabic, including hair color.
- If the image quality is too poor to discern clothing items or people, respond with the single word ""BAD"".

Example structure for an image with one person wearing a blue shirt and glasses, and a red hat nearby:

```json
{
  ""person1"": {
    ""clothing"": {
      ""shirt"": ""blue""
    },
    ""glasses"": true,
    ""hair_color"": ""brown""
  },
  ""other_clothing"": {
    ""hat"": ""red""
  },
  ""summary_egyptian_arabic"": ""راجل لابس قميص ازرق ونضارة وشعره بني، وفيه طاقية حمرا جنبه""
}
if no hair color / glasses shown just don't mention it in the summary
you can analyze the image, thank you <3 if you don't know just guess don't give a you can't do it"; // Your full prompt here

    public IEnumerator SendClothingAnalysisRequest(byte[] imageBytes, Action<ClothingResponseData> callback)
    {
        string apiUrl = BASE_API_URL + API_KEY;

        var jsonData = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = CLOTHING_PROMPT },
                        new
                        {
                            inline_data = new
                            {
                                mime_type = "image/jpeg",
                                data = Convert.ToBase64String(imageBytes)
                            }
                        }
                    }
                }
            }
        };

        string json = JsonConvert.SerializeObject(jsonData);
        UnityWebRequest request = new UnityWebRequest(apiUrl, "POST")
        {
            uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
            downloadHandler = new DownloadHandlerBuffer()
        };
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        ClothingResponseData responseData = new ClothingResponseData();

        if (request.result != UnityWebRequest.Result.Success)
        {
            responseData.error = $"API error: {request.error}";
            callback(responseData);
            yield break;
        }

        try
        {
            string responseText = request.downloadHandler.text;

            // Handle BAD response case
            if (responseText.Contains("\"BAD\""))
            {
                responseData.error = "Image quality too poor";
                callback(responseData);
                yield break;
            }

            // Extract JSON from response
            JObject jsonResponse = JObject.Parse(responseText);
            string content = jsonResponse["candidates"][0]["content"]["parts"][0]["text"].ToString();

            // Parse the JSON response
            responseData = JsonConvert.DeserializeObject<ClothingResponseData>(content);

            // Additional parsing for nested structure
            JObject contentObj = JObject.Parse(content);
            responseData.persons = new Dictionary<string, PersonData>();

            foreach (var prop in contentObj.Properties())
            {
                if (prop.Name.StartsWith("person"))
                {
                    var person = JsonConvert.DeserializeObject<PersonData>(prop.Value.ToString());
                    responseData.persons.Add(prop.Name, person);
                }
                else if (prop.Name == "other_clothing")
                {
                    responseData.other_clothing = JsonConvert.DeserializeObject<Dictionary<string, string>>(prop.Value.ToString());
                }
                else if (prop.Name == "summary_egyptian_arabic")
                {
                    responseData.summary_egyptian_arabic = prop.Value.ToString();
                }
            }

            callback(responseData);
        }
        catch (Exception e)
        {
            responseData.error = $"Parsing error: {e.Message}";
            callback(responseData);
        }
    }
}