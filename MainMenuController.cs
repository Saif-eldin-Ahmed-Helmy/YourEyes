using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;

[RequireComponent(typeof(WebCamTextureToMatHelper))]
[RequireComponent(typeof(Renderer))]
public class MainMenuController : MonoBehaviour
{
    [Header("Server Settings")]
    public string serverUrl = "http://172.20.10.3:5000";
    public float pollInterval = 0.5f;

    [Header("WebCam Settings")]
    public int requestedWidth = 640;
    public int requestedHeight = 480;
    public bool requestedIsFrontFacing = false;

    [Header("Debug UI")]
    public Text debugText;

    private WebCamTextureToMatHelper webCamTextureToMatHelper;
    private Texture2D texture;
    private Mat bgrMat;
    private bool isInitialized = false;

    private bool isSelectingMode = false;
    private int modeIndex = -1;
    private Coroutine singleTapCoroutine;
    private int tapCount = 0;
    private float lastTapTime = 0f;
    private float tapTimeWindow = 2f;
    private readonly string[] modeNames = new string[] {
            "التعرف على المسافة",
            "التعرف على النقود",
            "التعرف على الملابس",
            "لغة الإشارة",
            "وصف المشهد",
            "التعرف على التعابير"

    };
    private readonly int[] modeKeys = new int[] { 1, 2, 3, 4, 5, 6 };

    private MonoBehaviour currentMode = null;

    void Awake()
    {
        webCamTextureToMatHelper = GetComponent<WebCamTextureToMatHelper>();
        webCamTextureToMatHelper.requestedWidth = requestedWidth;
        webCamTextureToMatHelper.requestedHeight = requestedHeight;
        webCamTextureToMatHelper.requestedIsFrontFacing = requestedIsFrontFacing;

#if UNITY_ANDROID && !UNITY_EDITOR
        webCamTextureToMatHelper.avoidAndroidFrontCameraLowLightIssue = true;
#endif
    }

    void OnEnable()
    {
        webCamTextureToMatHelper.onInitialized.AddListener(OnWebCamTextureToMatHelperInitialized);
        webCamTextureToMatHelper.onDisposed.AddListener(OnWebCamTextureToMatHelperDisposed);
        webCamTextureToMatHelper.onErrorOccurred.AddListener(OnWebCamTextureToMatHelperErrorOccurred);
    }

    void OnDisable()
    {
        webCamTextureToMatHelper.onInitialized.RemoveListener(OnWebCamTextureToMatHelperInitialized);
        webCamTextureToMatHelper.onDisposed.RemoveListener(OnWebCamTextureToMatHelperDisposed);
        webCamTextureToMatHelper.onErrorOccurred.RemoveListener(OnWebCamTextureToMatHelperErrorOccurred);
    }

    void Start()
    {
        TTSManager.Speak("مرحبا");
        webCamTextureToMatHelper.Play();
        Log("مرحبا");
        StartCoroutine(InitCameraRoutine());
        ActivateMode(null);
        StartCoroutine(PollServer());
    }

    private IEnumerator InitCameraRoutine()
    {
        Log("Starting initialization...");

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            Log("Requesting camera permission...");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            float timeout = 5f;
            float timer = 0f;
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && timer < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                timer += 0.5f;
            }
        }

        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
        {
            Log("Camera permission denied.");
            yield break;
        }
        Log("Camera permission granted.");
#endif

        yield return new WaitForEndOfFrame();
        Log("Initializing WebCam...");
        webCamTextureToMatHelper.Initialize();
    }

    IEnumerator PollServer()
    {
        while (true)
        {
            using (UnityWebRequest www = UnityWebRequest.Get($"{serverUrl}/command"))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success && www.responseCode == 200)
                {
                    string json = www.downloadHandler.text;
                    CommandData cmd = JsonUtility.FromJson<CommandData>(json);
                    HandleCommand(cmd);
                    Debug.Log($"Command received: key={cmd.key}, distance={cmd.distance}");
                }
            }
            yield return new WaitForSeconds(pollInterval);
        }
    }

    void Update()
    {
        if (webCamTextureToMatHelper.IsPlaying() && webCamTextureToMatHelper.DidUpdateThisFrame())
        {
            Mat rgbaMat = webCamTextureToMatHelper.GetMat();

            if (!rgbaMat.empty())
            {
                Utils.matToTexture2D(rgbaMat, texture);
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            float now = Time.time;
            if (now - lastTapTime < tapTimeWindow)
                tapCount++;
            else
                tapCount = 1;
            lastTapTime = now;
           // debugText.color = Color.red;
            Log($"Tap detected: tapCount={tapCount}, time={now}");

            if (tapCount == 1)
            {
                if (singleTapCoroutine != null)
                    StopCoroutine(singleTapCoroutine);
                singleTapCoroutine = StartCoroutine(DetectSingleTap());
            }
            else if (tapCount == 2)
            {
                if (singleTapCoroutine != null)
                    StopCoroutine(singleTapCoroutine);
                HandleDoubleTap();
                tapCount = 0;
            }
        }
    }

    IEnumerator DetectSingleTap()
    {
        yield return new WaitForSeconds(tapTimeWindow);
        if (tapCount == 1)
        {
            Log("Single tap confirmed");
            HandleSingleTap();
        }
        tapCount = 0;
    }

    void HandleSingleTap()
    {
        if (isSelectingMode && modeIndex >= 0)
        {
            int key = modeKeys[modeIndex];
            HandleCommand(new CommandData { key = key, distance = 0f });
            TTSManager.Speak($"تم اختيار {modeNames[modeIndex]}");
            isSelectingMode = false;
            Log($"Mode {modeNames[modeIndex]} confirmed.");
        }
    }

    void HandleDoubleTap()
    {
        if (!isSelectingMode)
        {
            isSelectingMode = true;
            modeIndex = 0;
            Log("Entering mode selection");
        }
        else
        {
            modeIndex = (modeIndex + 1) % modeNames.Length;
            Log($"Cycling mode: new modeIndex={modeIndex}");
        }
        TTSManager.Speak(modeNames[modeIndex]);
        Log($"Mode read out: {modeNames[modeIndex]}");
    }

    void HandleCommand(CommandData data)
    {
        if (data.key != 1)
        {
            int key = modeKeys[modeIndex];
            TTSManager.Speak($"تم اختيار {modeNames[modeIndex]}");
        }
        switch (data.key)
        {
            case 1:
                string words = ConvertNumberToArabic((int)data.distance);
                TTSManager.Speak(words + " سنتيمتر");
                Log($"Distance: {words} cm");
                break;
            case 2:
                ActivateMode(MoneyDetection.Instance);
                break;
            case 3:
                ActivateMode(ClothesAnalysis.Instance);
                break;
            case 4:
                ActivateMode(HandPoseEstimator.Instance);
                break;
            case 5:
                ActivateMode(SceneDescription.Instance);
                break;
            case 6:
                ActivateMode(FacialExpression.Instance);
                break;

        }
    }

    public void OnWebCamTextureToMatHelperInitialized()
    {
        Debug.Log("OnWebCamTextureToMatHelperInitialized");
            
        Mat webCamTextureMat = webCamTextureToMatHelper.GetMat();
        texture = new Texture2D(webCamTextureMat.cols(), webCamTextureMat.rows(), TextureFormat.RGBA32, false);
        Utils.matToTexture2D(webCamTextureMat, texture);

        GetComponent<Renderer>().material.mainTexture = texture;

        // Set camera to fit screen width while maintaining aspect ratio
        float cameraWidth = webCamTextureMat.cols();
        float cameraHeight = webCamTextureMat.rows();
        float cameraAspect = cameraWidth / cameraHeight;

        // Calculate orthographic size (half of the height)
        Camera.main.orthographicSize = cameraHeight / 2;

        // Adjust the quad's scale to match the camera view perfectly
        transform.localScale = new Vector3(cameraWidth, cameraHeight, 1);

        // Position the quad at the center of the camera's view
        transform.position = new Vector3(0, 0, 0);

        // Ensure camera is properly centered
        Camera.main.transform.position = new Vector3(0, 0, -10);
        Camera.main.orthographic = true;

        bgrMat = new Mat(webCamTextureMat.rows(), webCamTextureMat.cols(), CvType.CV_8UC3);
        isInitialized = true;
    }

    public void OnWebCamTextureToMatHelperDisposed()
    {
        Debug.Log("OnWebCamTextureToMatHelperDisposed");

        if (bgrMat != null)
            bgrMat.Dispose();

        if (texture != null)
        {
            Texture2D.Destroy(texture);
            texture = null;
        }
        isInitialized = false;
    }

    public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMatHelper.ErrorCode errorCode)
    {
        Debug.Log("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
    }

    public void OnPlayButtonClick()
    {
        webCamTextureToMatHelper.Play();
    }

    public void OnPauseButtonClick()
    {
        webCamTextureToMatHelper.Pause();
    }

    public void OnStopButtonClick()
    {
        webCamTextureToMatHelper.Stop();
    }

    public void OnChangeCameraButtonClick()
    {
        webCamTextureToMatHelper.requestedIsFrontFacing = !webCamTextureToMatHelper.requestedIsFrontFacing;
    }

    void ActivateMode(MonoBehaviour detectionSystem)
    {
        DeactivateAll();
        if (detectionSystem != null)
        {
            detectionSystem.GetType().GetMethod("ActivateMode")?.Invoke(detectionSystem, null);
            currentMode = detectionSystem;
        }
    }

    void DeactivateAll()
    {
        MoneyDetection.Instance?.Deactivate();
        ClothesAnalysis.Instance?.Deactivate();
        HandPoseEstimator.Instance?.DeactivateMode();
    }

    public string ConvertNumberToArabic(float num)
    {
        if (num == 0.0f)
        {
            return "صفر";
        }

        string[] arabicWords = {
        "صفر", "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة",
        "عشرة", "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر",
        "ثمانية عشر", "تسعة عشر"
    };

        string[] arabicTens = {
        "", "", "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"
    };

        string[] arabicHundreds = {
        "", "مئة", "مئتان", "ثلاثمئة", "أربعمئة", "خمسمئة", "ستمئة", "سبعمئة", "ثمانمئة", "تسعمئة"
    };

        int intPart = (int)num;
        string arabicIntPart = "";

        if (intPart >= 100)
        {
            int hundreds = intPart / 100;
            int remainder = intPart % 100;
            arabicIntPart = arabicHundreds[hundreds];

            if (remainder > 0)
            {
                arabicIntPart += " و " + ConvertRemainderToArabic(remainder, arabicWords, arabicTens);
            }
        }
        else if (intPart < 20)
        {
            arabicIntPart = arabicWords[intPart];
        }
        else
        {
            int tens = intPart / 10;
            int ones = intPart % 10;

            if (ones == 0)
            {
                arabicIntPart = arabicTens[tens];
            }
            else
            {
                arabicIntPart = arabicWords[ones] + " و " + arabicTens[tens];
            }
        }

        float decimalPart = num - intPart;
        if (decimalPart > 0)
        {
            string arabicDecimalPart = " فاصلة";
            string decimalString = decimalPart.ToString("F2").Substring(2);
            foreach (char digit in decimalString)
            {
                if (digit >= '0' && digit <= '9')
                {
                    arabicDecimalPart += " " + arabicWords[int.Parse(digit.ToString())];
                }
            }
            return arabicIntPart + arabicDecimalPart;
        }

        return arabicIntPart;
    }

    private string ConvertRemainderToArabic(int number, string[] words, string[] tens)
    {
        if (number < 20)
        {
            return words[number];
        }
        else
        {
            int tensDigit = number / 10;
            int onesDigit = number % 10;
            if (onesDigit == 0)
            {
                return tens[tensDigit];
            }
            else
            {
                return words[onesDigit] + " و " + tens[tensDigit];
            }
        }
    }

    void Log(string message)
    {
        Debug.Log(message);
        //if (debugText != null)
          //  debugText.text += message + "\n";
    }

    void OnDestroy()
    {
        webCamTextureToMatHelper.Dispose();
        Utils.setDebugMode(false);
    }
}

[System.Serializable]
public class CommandData { public int key; public float distance; }