#if !UNITY_WSA_10_0
using UnityEngine;
using Unity.Barracuda;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.UnityUtils.Helper;
using OpenCVForUnity.DnnModule;
using OpenCVForUnity.ImgprocModule;
using System;
using System.Collections.Generic;
using System.Linq;
using OpenCVForUnity.UnityUtils;
using OpenCVForUnity.DnnModel;

public class HandPoseEstimator : MonoBehaviour
{
    private static HandPoseEstimator _instance;
    public static HandPoseEstimator Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HandPoseEstimator");
                _instance = go.AddComponent<HandPoseEstimator>();
                DontDestroyOnLoad(go);
            }
            return _instance;
        }
    }

    WebCamTextureToMatHelper webCamHelper;
    MediaPipePalmDetector palmDetector;
    MediaPipeHandPoseEstimator handPoseEstimator;
    IWorker poseWorker;
    bool isActive;
    string prevPose = "";
    float poseStartTime;
    float lastAnnounceTime;

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
            return;
        }
    }

    void Start()
    {
        webCamHelper = FindObjectOfType<WebCamTextureToMatHelper>();

        // --- load MediaPipe ONNX detectors just like the big class ---
        string palmPath = Utils.getFilePath("OpenCVForUnity/dnn/palm_detection_mediapipe_2023feb.onnx");
        palmDetector = new MediaPipePalmDetector(palmPath, 0.3f, 0.6f);
        string handPath = Utils.getFilePath("OpenCVForUnity/dnn/handpose_estimation_mediapipe_2023feb.onnx");
        handPoseEstimator = new MediaPipeHandPoseEstimator(handPath, 0.9f);

        // --- load the classifier (no shape check) ---
        var modelAsset = Resources.Load<NNModel>("Models/hand_pose_estim");
        if (modelAsset == null)
        {
            Debug.LogError("Could not find Resources/Models/hand_pose_estim.nn");
            return;
        }
        var model = ModelLoader.Load(modelAsset);
        poseWorker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, model);
    }

    public void ActivateMode() => isActive = true;
    public void DeactivateMode() => isActive = false;

    void Update()
    {
        if (!isActive || !webCamHelper.IsPlaying() || !webCamHelper.DidUpdateThisFrame())
            return;

        // grab RGBA frame, convert to BGR
        Mat rgba = webCamHelper.GetMat();
        using var bgr = new Mat();
        Imgproc.cvtColor(rgba, bgr, Imgproc.COLOR_RGBA2BGR);

        // detect palms
        using var palms = palmDetector.infer(bgr);
        for (int i = 0; i < palms.rows(); i++)
        {
            using var hand = handPoseEstimator.infer(bgr, palms.row(i));
            if (hand.empty()) continue;

            // exactly as in your big class: get row 4, col 0 → 21 landmarks × (x,y,z)
            float[] coords3D = new float[63];
            hand.get(4, 0, coords3D);

            // build 42‐length list of (x-ox, y-oy)
            var rel = new List<float>(42);
            float ox = coords3D[0], oy = coords3D[1];
            for (int k = 0; k < coords3D.Length; k += 3)
            {
                rel.Add(coords3D[k] - ox);
                rel.Add(coords3D[k + 1] - oy);
            }

            // normalize to [-1, 1]
            float maxAbs = rel.Max(Math.Abs);
            if (maxAbs == 0f) continue;
            for (int k = 0; k < rel.Count; k++)
                rel[k] /= maxAbs;

            // classify exactly like original:
            using var inputTensor = new Tensor(1, 1, 1, rel.Count, rel.ToArray());
            using var outputTensor = poseWorker.Execute(inputTensor).PeekOutput();
            var scores = outputTensor.ToReadOnlyArray();
            if (scores.Length == 0) continue;
            int best = Array.IndexOf(scores, scores.Max());

            // map to Arabic labels:
            string pose = best switch
            {
                0 => "أهلاً",             // hello
                1 => "نعم",              // yes
                2 => "أشير",             // point (verb form = "I point")
                3 => "حسنًا",            // ok
                4 => "جيد",              // good
                5 => "آسف",              // sorry
                6 => "حرف سي",           // C (say as "letter C")
                7 => "سيء",              // bad
                _ => "مفتوح",            // open
            };

            // announce once per stable pose (1.5s), with 5s cooldown
            if (pose != prevPose)
            {
                prevPose = pose;
                poseStartTime = Time.time;
            }
            else if (Time.time - poseStartTime >= 1.5f
                  && Time.time - lastAnnounceTime >= 5f)
            {
                TTSManager.Speak(pose);
                lastAnnounceTime = Time.time;
            }
        }
    }

    void OnDestroy()
    {
        poseWorker?.Dispose();
        palmDetector?.dispose();
        handPoseEstimator?.dispose();
    }
}
#endif
