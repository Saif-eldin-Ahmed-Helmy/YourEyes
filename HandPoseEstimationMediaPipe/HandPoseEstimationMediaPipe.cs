#if !UNITY_WSA_10_0

using OpenCVForUnity.CoreModule;
using OpenCVForUnity.DnnModule;
using OpenCVForUnity.ImgcodecsModule;
using OpenCVForUnity.ImgprocModule;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.UnityUtils.Helper;
using OpenCVForUnity.DnnModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Barracuda;
using UnityEditor;

namespace OpenCVForUnity
{
    /// <summary>
    /// Hand Pose Estimation MediaPipe
    /// Referring to https://github.com/opencv/opencv_zoo/tree/master/models/handpose_estimation_mediapipe
    /// </summary>
    [RequireComponent(typeof(WebCamTextureToMatHelper))]
    public class HandPoseEstimationMediaPipe : MonoBehaviour
    {
        /// <summary>
        /// The show Skeleton toggle.
        /// </summary>
        public Toggle showSkeletonToggle;

        public Text debugText;

        public bool showSkeleton;

        public MediaPipeHandPoseSkeletonVisualizer skeletonVisualizer;

        [Header("TEST")]

        [TooltipAttribute("Path to test input image.")]
        public string testInputImage;

        /// <summary>
        /// The texture.
        /// </summary>
        Texture2D texture;

        /// <summary>
        /// The webcam texture to mat helper.
        /// </summary>
        WebCamTextureToMatHelper webCamTextureToMatHelper;

        /// <summary>
        /// The bgr mat.
        /// </summary>
        Mat bgrMat;

        /// <summary>
        /// The palm detector.
        /// </summary>
        MediaPipePalmDetector palmDetector;

        /// <summary>
        /// The handpose estimator.
        /// </summary>
        MediaPipeHandPoseEstimator handPoseEstimator;

        /// <summary>
        /// The FPS monitor.
        /// </summary>
        FpsMonitor fpsMonitor;

        /// <summary>
        /// PALM_DETECTION_MODEL_FILENAME
        /// </summary>
        protected static readonly string PALM_DETECTION_MODEL_FILENAME = "OpenCVForUnity/dnn/palm_detection_mediapipe_2023feb.onnx";

        /// <summary>
        /// The palm detection model filepath.
        /// </summary>
        string palm_detection_model_filepath;

        /// <summary>
        /// HANDPOSE_ESTIMATION_MODEL_FILENAME
        /// </summary>
        protected static readonly string HANDPOSE_ESTIMATION_MODEL_FILENAME = "OpenCVForUnity/dnn/handpose_estimation_mediapipe_2023feb.onnx";

        /// <summary>
        /// The handpose estimation model filepath.
        /// </summary>
        string handpose_estimation_model_filepath;

        string modelPath = "Assets/Models/model.onnx";

        IWorker worker;



#if UNITY_WEBGL
        IEnumerator getFilePath_Coroutine;
#endif

        // Use this for initialization
        void Start()
        {
            fpsMonitor = GetComponent<FpsMonitor>();

            webCamTextureToMatHelper = gameObject.GetComponent<WebCamTextureToMatHelper>();

            // Update GUI state
            showSkeletonToggle.isOn = showSkeleton;

#if UNITY_WEBGL
            getFilePath_Coroutine = GetFilePath();
            StartCoroutine(getFilePath_Coroutine);
#else
            palm_detection_model_filepath = Utils.getFilePath(PALM_DETECTION_MODEL_FILENAME);
            handpose_estimation_model_filepath = Utils.getFilePath(HANDPOSE_ESTIMATION_MODEL_FILENAME);
            try
            {
                var modelAsset = (NNModel)Resources.Load("Models/hand_pose_estim", typeof(NNModel));
                var model = ModelLoader.Load(modelAsset);
                worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
                //debugText.text = "Model Loaded";
            }
            catch (Exception ex)
            {
              //  debugText.text = ex.ToString();
            }
            Run();
#endif
        }

#if UNITY_WEBGL
        private IEnumerator GetFilePath()
        {
            var getFilePathAsync_0_Coroutine = Utils.getFilePathAsync(PALM_DETECTION_MODEL_FILENAME, (result) =>
            {
                palm_detection_model_filepath = result;
            });
            yield return getFilePathAsync_0_Coroutine;

            var getFilePathAsync_1_Coroutine = Utils.getFilePathAsync(HANDPOSE_ESTIMATION_MODEL_FILENAME, (result) =>
            {
                handpose_estimation_model_filepath = result;
            });
            yield return getFilePathAsync_1_Coroutine;

            getFilePath_Coroutine = null;

            Run();
        }
#endif

        // Use this for initialization
        void Run()
        {
            //if true, The error log of the Native side OpenCV will be displayed on the Unity Editor Console.
            Utils.setDebugMode(true);


            if (string.IsNullOrEmpty(palm_detection_model_filepath))
            {
                Debug.LogError(PALM_DETECTION_MODEL_FILENAME + " is not loaded. Please read “StreamingAssets/OpenCVForUnity/dnn/setup_dnn_module.pdf” to make the necessary setup.");
            }
            else
            {
                palmDetector = new MediaPipePalmDetector(palm_detection_model_filepath, 0.3f, 0.6f);
            }

            if (string.IsNullOrEmpty(handpose_estimation_model_filepath))
            {
                Debug.LogError(HANDPOSE_ESTIMATION_MODEL_FILENAME + " is not loaded. Please read “StreamingAssets/OpenCVForUnity/dnn/setup_dnn_module.pdf” to make the necessary setup.");
            }
            else
            {
                handPoseEstimator = new MediaPipeHandPoseEstimator(handpose_estimation_model_filepath, 0.9f);
            }


            if (string.IsNullOrEmpty(testInputImage))
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                // Avoids the front camera low light issue that occurs in only some Android devices (e.g. Google Pixel, Pixel2).
                webCamTextureToMatHelper.avoidAndroidFrontCameraLowLightIssue = true;
#endif
                webCamTextureToMatHelper.Initialize();
            }
            else
            {
                /////////////////////
                // TEST

                var getFilePathAsync_0_Coroutine = Utils.getFilePathAsync("OpenCVForUnity/dnn/" + testInputImage, (result) =>
                {
                    string test_input_image_filepath = result;
                    if (string.IsNullOrEmpty(test_input_image_filepath)) Debug.Log("The file:" + testInputImage + " did not exist in the folder “Assets/StreamingAssets/OpenCVForUnity/dnn”.");

                    Mat img = Imgcodecs.imread(test_input_image_filepath);
                    if (img.empty())
                    {
                        img = new Mat(424, 640, CvType.CV_8UC3, new Scalar(0, 0, 0));
                        Imgproc.putText(img, testInputImage + " is not loaded.", new Point(5, img.rows() - 30), Imgproc.FONT_HERSHEY_SIMPLEX, 0.7, new Scalar(255, 255, 255, 255), 2, Imgproc.LINE_AA, false);
                        Imgproc.putText(img, "Please read console message.", new Point(5, img.rows() - 10), Imgproc.FONT_HERSHEY_SIMPLEX, 0.7, new Scalar(255, 255, 255, 255), 2, Imgproc.LINE_AA, false);
                    }
                    else
                    {
                        TickMeter tm = new TickMeter();
                        tm.start();

                        Mat palms = palmDetector.infer(img);

                        tm.stop();
                        Debug.Log("MediaPipePalmDetector Inference time (preprocess + infer + postprocess), ms: " + tm.getTimeMilli());

                        List<Mat> hands = new List<Mat>();

                        // Estimate the pose of each hand
                        for (int i = 0; i < palms.rows(); ++i)
                        {
                            tm.reset();
                            tm.start();

                            // Handpose estimator inference
                            Mat handpose = handPoseEstimator.infer(img, palms.row(i));

                            int num_landmarks = handpose.cols() / 3; // Each landmark is (x, y, z)
                            float[] landmarks_coords = new float[handpose.cols()];

                            // Extract and process 3D landmarks
                            handpose.get(1, 0, landmarks_coords); // Adjust row index if needed

                            List<Vector3> landmarks = new List<Vector3>();
                            for (int k = 0; k < landmarks_coords.Length; k += 3)
                            {
                                Vector3 point = new Vector3(landmarks_coords[k], landmarks_coords[k + 1], landmarks_coords[k + 2]);
                                landmarks.Add(point);
                            }

                            // Print all 3D landmarks
                            Debug.Log("3D Landmarks Coordinates: ");
                            for (int k = 0; k < landmarks.Count; k++)
                            {
                                Debug.Log($"Landmark {k}: X={landmarks[k].x}, Y={landmarks[k].y}, Z={landmarks[k].z}");
                            }
                            string landmarksString = string.Join(", ", landmarks_coords);
                            Debug.Log("3D Landmarks Coordinates (string): " + landmarksString);
                            debugText.text = "3D Landmarks Coordinates: " + landmarksString;



                            tm.stop();
                            Debug.Log("MediaPipeHandPoseEstimator Inference time (preprocess + infer + postprocess), ms: " + tm.getTimeMilli());

                            if (!handpose.empty())
                                hands.Add(handpose);
                        }
                        //palmDetector.visualize(img, palms, true, false);

                      //  foreach (var hand in hands)
                    //        handPoseEstimator.visualize(img, hand, true, false);

                        if (skeletonVisualizer != null && skeletonVisualizer.showSkeleton)
                        {
                            if (hands.Count > 0 && !hands[0].empty())
                                skeletonVisualizer.UpdatePose(hands[0]);
                        }
                    }

                    gameObject.transform.localScale = new Vector3(img.width(), img.height(), 1);
                    float imageWidth = img.width();
                    float imageHeight = img.height();
                    float widthScale = (float)Screen.width / imageWidth;
                    float heightScale = (float)Screen.height / imageHeight;
                    if (widthScale < heightScale)
                    {
                        Camera.main.orthographicSize = (imageWidth * (float)Screen.height / (float)Screen.width) / 2;
                    }
                    else
                    {
                        Camera.main.orthographicSize = imageHeight / 2;
                    }

                    Imgproc.cvtColor(img, img, Imgproc.COLOR_BGR2RGB);
                    Texture2D texture = new Texture2D(img.cols(), img.rows(), TextureFormat.RGB24, false);
                    Utils.matToTexture2D(img, texture);
                    gameObject.GetComponent<Renderer>().material.mainTexture = texture;

                });
                StartCoroutine(getFilePathAsync_0_Coroutine);

                /////////////////////
            }
        }

        /// <summary>
        /// Raises the webcam texture to mat helper initialized event.
        /// </summary>
        public void OnWebCamTextureToMatHelperInitialized()
        {
            Debug.Log("OnWebCamTextureToMatHelperInitialized");

            Mat webCamTextureMat = webCamTextureToMatHelper.GetMat();

            texture = new Texture2D(webCamTextureMat.cols(), webCamTextureMat.rows(), TextureFormat.RGBA32, false);
            Utils.matToTexture2D(webCamTextureMat, texture);

            gameObject.GetComponent<Renderer>().material.mainTexture = texture;

            gameObject.transform.localScale = new Vector3(webCamTextureMat.cols(), webCamTextureMat.rows(), 1);
            Debug.Log("Screen.width " + Screen.width + " Screen.height " + Screen.height + " Screen.orientation " + Screen.orientation);

            if (fpsMonitor != null)
            {
                fpsMonitor.Add("width", webCamTextureMat.width().ToString());
                fpsMonitor.Add("height", webCamTextureMat.height().ToString());
                fpsMonitor.Add("orientation", Screen.orientation.ToString());
            }


            float width = webCamTextureMat.width();
            float height = webCamTextureMat.height();

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

            bgrMat = new Mat(webCamTextureMat.rows(), webCamTextureMat.cols(), CvType.CV_8UC3);
        }

        /// <summary>
        /// Raises the webcam texture to mat helper disposed event.
        /// </summary>
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
        }

        /// <summary>
        /// Raises the webcam texture to mat helper error occurred event.
        /// </summary>
        /// <param name="errorCode">Error code.</param>
        public void OnWebCamTextureToMatHelperErrorOccurred(WebCamTextureToMatHelper.ErrorCode errorCode)
        {
            Debug.Log("OnWebCamTextureToMatHelperErrorOccurred " + errorCode);
        }

        public float[] Classify(float[] landmarkList)
        {
            float[] results = new float[2] { -1, -1 };

            if (this.worker == null)
            {
                Debug.LogError("Model session is not initialized. Returning default output.");
                return results; // Return default value to indicate failure
            }

            Tensor inputTensor = null; // Declare inputTensor for disposal
            Tensor outputTensor = null; // Declare outputTensor for disposal

            try
            {
                // Prepare the input tensor (convert landmark list to tensor format)
                inputTensor = new Tensor(1, 1, 1, landmarkList.Length, landmarkList); // Assuming landmarkList is already flattened

                // Run inference
                outputTensor = worker.Execute(inputTensor).PeekOutput();

                // Check if the result is valid
                if (outputTensor == null)
                {
                    Debug.LogError("Inference failed, result is null.");
                    return results; // Return default value in case of failure
                }

                // Get the output data as an array
                var result = outputTensor.ToReadOnlyArray();
                int resultIndex = Array.IndexOf(result, Mathf.Max(result));

                // Debugging details
                Debug.Log($"Result Index: {resultIndex}");
                Debug.Log($"Input Data (Landmark List): {string.Join(", ", landmarkList)}");
                Debug.Log($"Output: {string.Join(", ", result)}");

                // Handle confidence threshold
                if (result[resultIndex] < 0.3)
                {
                    return results;
                }

                results[0] = resultIndex;
                results[1] = result[resultIndex];
                return results;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during inference: {e.Message}");
                return results; // Return default value to indicate failure
            }
            finally
            {
                // Ensure tensors are properly disposed
                if (inputTensor != null) inputTensor.Dispose();
                if (outputTensor != null) outputTensor.Dispose();
            }
        }

        // Update is called once per frame
        private DateTime lastPoseUpdateTime = DateTime.MinValue; // Tracks the last time Classify was called
        private string cachedPose = ""; // Cached recognized pose
        private float cachedConfidence = 0f; // Cached confidence score

        void Update()
        {
            if (webCamTextureToMatHelper.IsPlaying() && webCamTextureToMatHelper.DidUpdateThisFrame())
            {
                Mat rgbaMat = webCamTextureToMatHelper.GetMat();

                if (palmDetector == null || handPoseEstimator == null)
                {
                    Imgproc.putText(rgbaMat, "model file is not loaded.", new Point(5, rgbaMat.rows() - 30), Imgproc.FONT_HERSHEY_SIMPLEX, 0.7, new Scalar(255, 255, 255, 255), 2, Imgproc.LINE_AA, false);
                    Imgproc.putText(rgbaMat, "Please read console message.", new Point(5, rgbaMat.rows() - 10), Imgproc.FONT_HERSHEY_SIMPLEX, 0.7, new Scalar(255, 255, 255, 255), 2, Imgproc.LINE_AA, false);
                }
                else
                {
                    Imgproc.cvtColor(rgbaMat, bgrMat, Imgproc.COLOR_RGBA2BGR);

                    Mat palms = palmDetector.infer(bgrMat);
                    List<Mat> hands = new List<Mat>();

                    for (int i = 0; i < palms.rows(); ++i)
                    {
                        Mat handpose = handPoseEstimator.infer(bgrMat, palms.row(i));
                        try
                        {
                            if (handpose.empty()) continue;

                            // Extract landmarks
                            float[] landmarksArray = new float[63];
                            handpose.get(4, 0, landmarksArray);

                            // Normalize landmarks
                            List<float> relativeLandmarks = new List<float>();
                            float originX = landmarksArray[0];
                            float originY = landmarksArray[1];

                            for (int k = 0; k < landmarksArray.Length; k += 3)
                            {
                                float relX = landmarksArray[k] - originX;
                                float relY = landmarksArray[k + 1] - originY;
                                relativeLandmarks.Add(relX);
                                relativeLandmarks.Add(relY);
                            }

                            float maxAbsValue = relativeLandmarks.Max(value => Math.Abs(value));
                            for (int k = 0; k < relativeLandmarks.Count; k++)
                            {
                                relativeLandmarks[k] /= maxAbsValue;
                            }

                            // Only classify if at least one second has passed since the last classification
                            if ((DateTime.Now - lastPoseUpdateTime).TotalSeconds >= 3)
                            {
                                //lastPoseUpdateTime = DateTime.Now;
                                float[] normalized = relativeLandmarks.ToArray();
                                float[] classifiedPose = Classify(normalized);

                                // Update the cached values
                                int pose = (int)classifiedPose[0];
                                cachedPose = pose == 0 ? "Hello" : pose == 1 ? "Yes" : pose == 2 ? "Pointer" : pose == 3 ? "OK" : pose == 4 ? "Good" : pose == 5 ? "Sorry" : pose == 6 ? "C" : pose == 7 ? "Bad" : "Open";
                                cachedConfidence = classifiedPose[1];
                            }

                            // Use cached pose for visualization
                            if (!handpose.empty())
                            {
                                hands.Add(handpose);
                                Imgproc.cvtColor(bgrMat, rgbaMat, Imgproc.COLOR_BGR2RGBA);
                                handPoseEstimator.visualize(rgbaMat, handpose, cachedPose, cachedConfidence, false, true);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.Log($"HandPoseEstimationMediaPipeExample Exception: {e}");
                        }
                    }

                    if (skeletonVisualizer != null && skeletonVisualizer.showSkeleton)
                    {
                        if (hands.Count > 0 && !hands[0].empty())
                            skeletonVisualizer.UpdatePose(hands[0]);
                    }
                }

                Utils.matToTexture2D(rgbaMat, texture);
            }
        }


        /// <summary>
        /// Raises the destroy event.
        /// </summary>
        void OnDestroy()
        {
            webCamTextureToMatHelper.Dispose();

            if (palmDetector != null)
                palmDetector.dispose();

            if (handPoseEstimator != null)
                handPoseEstimator.dispose();

            Utils.setDebugMode(false);

#if UNITY_WEBGL
            if (getFilePath_Coroutine != null)
            {
                StopCoroutine(getFilePath_Coroutine);
                ((IDisposable)getFilePath_Coroutine).Dispose();
            }
#endif
        }

        /// <summary>
        /// Raises the back button click event.
        /// </summary>
        public void OnBackButtonClick()
        {
            SceneManager.LoadScene("YourEyes");
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

        /// <summary>
        /// Raises the change camera button click event.
        /// </summary>
        public void OnChangeCameraButtonClick()
        {
            webCamTextureToMatHelper.requestedIsFrontFacing = !webCamTextureToMatHelper.requestedIsFrontFacing;
        }

        /// <summary>
        /// Raises the show skeleton toggle value changed event.
        /// </summary>
        public void OnShowSkeletonToggleValueChanged()
        {
            if (showSkeletonToggle.isOn != showSkeleton)
            {
                showSkeleton = showSkeletonToggle.isOn;
                skeletonVisualizer.showSkeleton = showSkeletonToggle.isOn;
            }
        }
    }
}

#endif