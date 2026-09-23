using Newtonsoft.Json;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgcodecsModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using OpenCVForUnity;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Text;

namespace RealTimeFaceRecognitionExample
{
    [RequireComponent(typeof(WebCamTextureToMatHelper))]
    public class MoneyRecognition : MonoBehaviour
    {
        [SerializeField] private string backendUrl = "http://127.0.0.1:5000/gemini/generate";

        public Text moneyInfo;
        public Text billsInfo;

        /// <summary>
        /// Determines if debug mode.
        /// </summary>
        public bool isDebugMode = false;

        Mat yuvMat;
        Mat yMat;

        Mat displayMat;
        Mat inputDisplayAreaMat;

        /// <summary>
        /// The texture.
        /// </summary>
        Texture2D texture;

        /// <summary>
        /// The webcam texture to mat helper.
        /// </summary>
        WebCamTextureToMatHelper webCamTextureToMatHelper;

        /// <summary>
        /// The FPS monitor.
        /// </summary>
        FpsMonitor fpsMonitor;

        // Use this for initialization
        void Start()
        {
            fpsMonitor = GetComponent<FpsMonitor>();
            webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper>();
#if UNITY_ANDROID && !UNITY_EDITOR
            // Avoids the front camera low light issue that occurs in only some Android devices (e.g. Google Pixel, Pixel2).
            webCamTextureToMatHelper.avoidAndroidFrontCameraLowLightIssue = true;
#endif
            webCamTextureToMatHelper.Initialize();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper initialized event.
        /// </summary>
        public void OnWebCamTextureToMatHelperInitialized()
        {
            Debug.Log("OnWebCamTextureToMatHelperInitialized");

            Mat webCamTextureMat = webCamTextureToMatHelper.GetMat();

            if (webCamTextureMat.width() < webCamTextureMat.height())
            {
                displayMat = new Mat(webCamTextureMat.rows(), webCamTextureMat.cols() * 2, webCamTextureMat.type(), new Scalar(0, 0, 0, 255));
                inputDisplayAreaMat = new Mat(displayMat, new OpenCVForUnity.CoreModule.Rect(0, 0, webCamTextureMat.width(), webCamTextureMat.height()));
            }
            else
            {
                displayMat = new Mat(webCamTextureMat.rows() * 2, webCamTextureMat.cols(), webCamTextureMat.type(), new Scalar(0, 0, 0, 255));
                inputDisplayAreaMat = new Mat(displayMat, new OpenCVForUnity.CoreModule.Rect(0, 0, webCamTextureMat.width(), webCamTextureMat.height()));
            }

            texture = new Texture2D(displayMat.cols(), displayMat.rows(), TextureFormat.RGBA32, false);

            gameObject.GetComponent<Renderer>().material.mainTexture = texture;

            gameObject.transform.localScale = new Vector3(displayMat.cols(), displayMat.rows(), 1);

            Debug.Log("Screen.width " + Screen.width + " Screen.height " + Screen.height + " Screen.orientation " + Screen.orientation);

            if (fpsMonitor != null)
            {
                fpsMonitor.Add("width", displayMat.width().ToString());
                fpsMonitor.Add("height", displayMat.height().ToString());
                fpsMonitor.Add("orientation", Screen.orientation.ToString());
                fpsMonitor.consoleText = "Please place money on the table that's visible and click recognize.";
            }

            float width = displayMat.width();
            float height = displayMat.height();

            float widthScale = (float)Screen.width / width;
            float heightScale = (float)Screen.height / height;
            if (widthScale < heightScale)
            {
                Camera.main.orthographicSize = (width * (float)Screen.height / (float)Screen.width) / 2;
            }
            else
            {
                Camera.main.orthographicSize = height / 2;
            }

            yuvMat = new Mat();
            yMat = new Mat();

            // If the WebCam is front facing, flip the Mat horizontally. Required for successful detection of document.
            if (webCamTextureToMatHelper.IsFrontFacing() && !webCamTextureToMatHelper.flipHorizontal)
            {
                webCamTextureToMatHelper.flipHorizontal = true;
            }
            else if (!webCamTextureToMatHelper.IsFrontFacing() && webCamTextureToMatHelper.flipHorizontal)
            {
                webCamTextureToMatHelper.flipHorizontal = false;
            }
        }

        /// <summary>
        /// Raises the web cam texture to mat helper disposed event.
        /// </summary>
        public void OnWebCamTextureToMatHelperDisposed()
        {
            Debug.Log("OnWebCamTextureToMatHelperDisposed");

            if (texture != null)
            {
                Texture2D.Destroy(texture);
                texture = null;
            }

            if (yuvMat != null)
                yuvMat.Dispose();

            if (yMat != null)
                yMat.Dispose();

            if (displayMat != null)
                displayMat.Dispose();
        }

        /// <summary>
        /// Raises the web cam texture to mat helper error occurred event.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMatHelper.ErrorCode errorCode)
        {
            Debug.Log("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
        }

        // Update is called once per frame
        void Update()
        {
            if (webCamTextureToMatHelper.IsPlaying() && webCamTextureToMatHelper.DidUpdateThisFrame())
            {
                Mat rgbaMat = webCamTextureToMatHelper.GetMat();

                rgbaMat.copyTo(inputDisplayAreaMat);

                Utils.matToTexture2D(displayMat, texture, true, 0, true);
            }
        }

        /// <summary>
        /// Raises the destroy event.
        /// </summary>
        void OnDestroy()
        {
            webCamTextureToMatHelper.Dispose();
        }

        /// <summary>
        /// Raises the back button click event.
        /// </summary>
        public void OnBackButtonClick()
        {
            SceneManager.LoadScene("YourEyes");
        }

        public void OnRecognizeButtonClick()
        {
            if (webCamTextureToMatHelper.IsPlaying())
            {
                Mat rgbaMat = webCamTextureToMatHelper.GetMat();

                MatOfByte bytes = new MatOfByte();
                Imgcodecs.imencode(".png", rgbaMat, bytes);
                byte[] imageBytes = bytes.toArray();

                // Send image to the application backend
                StartCoroutine(SendImageToGemini(imageBytes));
            }
        }

        IEnumerator SendImageToGemini(byte[] imageBytes)
        {
            moneyInfo.text = "Processing...";
            billsInfo.text = "";
            // Send the image to the application backend.
            string apiUrl = backendUrl;

            // Create JSON payload with both the text prompt and encoded image
            var jsonData = new
            {
                contents = new[]
                {
            new
            {
                parts = new object[]
                {
                    new { text = "This photo has Egyptian money. I want to know how much money is in the photo. Return exactly four lines, one field per line: Are You Sure: Yes/No (Yes if picture is good, No if you'd prefer a better picture), Is There Money: Yes/No (Yes if there's money in the pictue, No if the picture doesn't have any money), Money Notes & Coins: [1, 1, 10, 10, 200, etc.] (Empty if there's no money), Money Total: 220 (0 if there's no money)." },
                    new
                    {
                        inline_data = new
                        {
                            mime_type = "image/png",
                            data = Convert.ToBase64String(imageBytes) // Convert image to Base64
                        }
                    }
                }
            }
        }
            };

            // Serialize the payload to JSON
            string json = JsonConvert.SerializeObject(jsonData);

            // Create the UnityWebRequest for a POST request
            UnityWebRequest request = new UnityWebRequest(apiUrl, "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };

            // Set request headers
            request.SetRequestHeader("Content-Type", "application/json");

            // Send the request and wait for the response
            yield return request.SendWebRequest();

            // Handle the response
            if (request.result == UnityWebRequest.Result.Success)
            {
                var responseText = request.downloadHandler.text;

                try
                {
                    var response = Newtonsoft.Json.Linq.JObject.Parse(responseText);
                    string text = response["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Empty analysis result");
                    string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length < 4) throw new FormatException("Incomplete analysis result");
                    string[] values = new string[4];
                    for (int i = 0; i < 4; i++)
                    {
                        int separator = lines[i].IndexOf(':');
                        if (separator < 0) throw new FormatException("Invalid analysis field");
                        values[i] = lines[i].Substring(separator + 1).Trim();
                    }
                    moneyInfo.text = $"Are You Sure: {values[0]}\nIs There Money: {values[1]}";
                    billsInfo.text = $"Notes & Coins: {values[2]}\nTotal: {values[3]}";
                }
                catch (Exception)
                {
                    moneyInfo.text = "Unable to read the analysis result. Please try again.";
                    billsInfo.text = "";
                }
            }
            else
            {
                // Log the error and display a message
                Debug.LogError("Request failed: " + request.error);
                moneyInfo.text = "Error: " + request.error;
            }
        }

        /// <summary>
        /// Raises the play button click event.
        /// </summary>
        public void OnPlayButtonClick()
        {
            webCamTextureToMatHelper.Play();
        }

        /// <summary>
        /// Raises the pause button click event.
        /// </summary>
        public void OnPauseButtonClick()
        {
            webCamTextureToMatHelper.Pause();
        }

        /// <summary>
        /// Raises the stop button click event.
        /// </summary>
        public void OnStopButtonClick()
        {
            webCamTextureToMatHelper.Stop();
        }

    }
}