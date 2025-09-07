using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class TTSManager : MonoBehaviour
{
    private static TTSManager _instance;
    public static TTSManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // Create a new GameObject if one doesn't already exist
                var go = new GameObject("TTSManager");
                _instance = go.AddComponent<TTSManager>();
            }
            return _instance;
        }
    }

    [Tooltip("Optional: assign an AudioSource in the inspector. If left blank, one will be added at runtime.")]
    public AudioSource audioSource;

    public string serviceUrl = "http://172.20.10.3:5000";

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // Ensure we have an AudioSource
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Call this from anywhere to speak Arabic text.
    /// </summary>
    public static void Speak(string arabicText)
    {
        Instance.StartCoroutine(Instance.HandleSpeak(arabicText));
    }

    private IEnumerator HandleSpeak(string text)
    {
        string url = $"{serviceUrl}/tts";

        // Prepare JSON payload
        var payload = JsonUtility.ToJson(new TextRequest { text = text });
        byte[] bodyRaw = Encoding.UTF8.GetBytes(payload);

        // Setup the POST request and tell it to expect a WAV clip back
        using (var uwr = new UnityWebRequest(url, "POST"))
        {
            uwr.uploadHandler = new UploadHandlerRaw(bodyRaw);
            uwr.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
            uwr.SetRequestHeader("Content-Type", "application/json");

            yield return uwr.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            if (uwr.result != UnityWebRequest.Result.Success)
#else
            if (uwr.isNetworkError || uwr.isHttpError)
#endif
            {
                Debug.LogError($"TTS request failed: {uwr.error}");
            }
            else
            {
                // Get and play the clip
                AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
                audioSource.clip = clip;
                audioSource.Play();
            }
        }
    }

    // Helper class to serialize the JSON body
    [System.Serializable]
    private class TextRequest
    {
        public string text;
    }
}
