using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Sentis;
using Gate2Reality.Narrative;

namespace Gate2Reality.Detection
{
    public class YoloObjectDetector : MonoBehaviour
    {
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private int inferenceHz = 5;
        [SerializeField] private int personOnlyHz = 1;
        [SerializeField] private float nmsIouThreshold = 0.45f;
        [SerializeField] private float scoreThreshold = 0.4f;
        [SerializeField] private int inputWidth = 640;
        [SerializeField] private int inputHeight = 640;
        [SerializeField] private Camera arCamera;

        public event Action<DetectionEvent> OnRawDetection;
        public event Action<bool> OnHumanPresenceChanged;

        private IWorker _worker;
        private RenderTexture _captureBuffer;
        private Tensor<float> _inputTensor;
        private bool _personOnlyMode;
        private int _effectiveHz;
        private float _inferenceInterval;
        private float _inferenceTimer;
        private bool _inferenceInFlight;
        private bool _lastHumanPresence;

        private Candidate[] _candidates;
        private int _candidateCount;
        private const int MaxCandidates = 8400;

        private static readonly int[] CocoToLabel = BuildCocoMap();

        private void Awake()
        {
            _candidates = new Candidate[MaxCandidates];
            _captureBuffer = new RenderTexture(inputWidth, inputHeight, 0, RenderTextureFormat.ARGB32);
            _effectiveHz = inferenceHz;
            _inferenceInterval = 1f / _effectiveHz;

            var model = ModelLoader.Load(modelAsset);
            _worker = WorkerFactory.CreateWorker(BackendType.GPUCompute, model);
        }

        private void OnDestroy()
        {
            _worker?.Dispose();
            _inputTensor?.Dispose();
            _captureBuffer?.Release();
        }

        public void SetPersonOnlyMode(bool enabled)
        {
            _personOnlyMode = enabled;
            _effectiveHz = enabled ? personOnlyHz : inferenceHz;
            _inferenceInterval = 1f / _effectiveHz;
        }

        public void SetInferenceInterval(int hz)
        {
            inferenceHz = hz;
            if (!_personOnlyMode)
            {
                _effectiveHz = hz;
                _inferenceInterval = 1f / hz;
            }
        }

        private void Update()
        {
            _inferenceTimer += Time.deltaTime;
            if (_inferenceTimer < _inferenceInterval) return;
            _inferenceTimer = 0f;

            if (!_inferenceInFlight && TryCaptureFrame())
                RunInference();
        }

        public bool TryCaptureFrame()
        {
            if (arCamera == null) return false;
            var prev = RenderTexture.active;
            arCamera.targetTexture = _captureBuffer;
            arCamera.Render();
            arCamera.targetTexture = null;
            RenderTexture.active = prev;
            return true;
        }

        private void RunInference()
        {
            _inputTensor?.Dispose();
            _inputTensor = TextureConverter.ToTensor(_captureBuffer, inputWidth, inputHeight, 3);
            _inferenceInFlight = true;
            _worker.Schedule(_inputTensor);
            var output = _worker.PeekOutput() as Tensor<float>;
            if (output != null)
            {
                output.ReadbackAndClone();
                PostProcess(output);
            }
            _inferenceInFlight = false;
        }

        public void PostProcess(Tensor<float> output)
        {
            _candidateCount = 0;
            int numBoxes = output.shape[2];

            for (int i = 0; i < numBoxes && _candidateCount < MaxCandidates; i++)
            {
                float cx = output[0, 0, i];
                float cy = output[0, 1, i];
                float w  = output[0, 2, i];
                float h  = output[0, 3, i];

                float bestScore = 0f;
                int bestClass = -1;
                for (int c = 4; c < output.shape[1]; c++)
                {
                    float s = output[0, c, i];
                    if (s > bestScore) { bestScore = s; bestClass = c - 4; }
                }

                if (bestScore < scoreThreshold) continue;
                if (_personOnlyMode && bestClass != 0) continue;

                _candidates[_candidateCount++] = new Candidate(cx, cy, w, h, bestScore, bestClass);
            }

            ApplyNms();
            EmitDetections();
        }

        public void ApplyNms()
        {
            for (int i = 0; i < _candidateCount; i++)
            {
                if (_candidates[i].Suppressed) continue;
                for (int j = i + 1; j < _candidateCount; j++)
                {
                    if (_candidates[j].Suppressed) continue;
                    if (_candidates[i].CocoClass != _candidates[j].CocoClass) continue;
                    if (IoU(ref _candidates[i], ref _candidates[j]) > nmsIouThreshold)
                    {
                        if (_candidates[j].Score < _candidates[i].Score)
                            _candidates[j] = _candidates[j].AsSuppressed();
                        else
                            _candidates[i] = _candidates[i].AsSuppressed();
                    }
                }
            }
        }

        public void EmitDetections()
        {
            bool humanSeen = false;
            for (int i = 0; i < _candidateCount; i++)
            {
                if (_candidates[i].Suppressed) continue;
                int labelIdx = _candidates[i].CocoClass < CocoToLabel.Length ? CocoToLabel[_candidates[i].CocoClass] : -1;
                if (labelIdx < 0) continue;

                var label = (NarrativeLabel)labelIdx;
                if (label == NarrativeLabel.None) continue;

                Vector2 screenUv = new Vector2(_candidates[i].Cx / inputWidth, _candidates[i].Cy / inputHeight);
                float radius = Mathf.Max(_candidates[i].W, _candidates[i].H) * 0.5f / inputWidth;

                var evt = new DetectionEvent(label, default, _candidates[i].Score, radius);
                OnRawDetection?.Invoke(evt);

                if (label == NarrativeLabel.None && _candidates[i].CocoClass == 0)
                    humanSeen = true;
            }

            if (humanSeen != _lastHumanPresence)
            {
                _lastHumanPresence = humanSeen;
                OnHumanPresenceChanged?.Invoke(humanSeen);
            }
        }

        private static float IoU(ref Candidate a, ref Candidate b)
        {
            float ax1 = a.Cx - a.W * 0.5f, ay1 = a.Cy - a.H * 0.5f;
            float ax2 = a.Cx + a.W * 0.5f, ay2 = a.Cy + a.H * 0.5f;
            float bx1 = b.Cx - b.W * 0.5f, by1 = b.Cy - b.H * 0.5f;
            float bx2 = b.Cx + b.W * 0.5f, by2 = b.Cy + b.H * 0.5f;

            float ix = Mathf.Max(0, Mathf.Min(ax2, bx2) - Mathf.Max(ax1, bx1));
            float iy = Mathf.Max(0, Mathf.Min(ay2, by2) - Mathf.Max(ay1, by1));
            float inter = ix * iy;
            float aArea = a.W * a.H;
            float bArea = b.W * b.H;
            return inter / (aArea + bArea - inter + 1e-6f);
        }

        private static int[] BuildCocoMap()
        {
            var map = new int[80];
            for (int i = 0; i < map.Length; i++) map[i] = -1;
            // COCO class 56 = chair, 73 = book, 41 = cup
            map[56] = (int)NarrativeLabel.Chair;
            map[73] = (int)NarrativeLabel.Book;
            map[41] = (int)NarrativeLabel.Cup;
            return map;
        }

        internal struct Candidate
        {
            public float Cx, Cy, W, H;
            public float Score;
            public int CocoClass;
            public bool Suppressed;

            public Candidate(float cx, float cy, float w, float h, float score, int cocoClass)
            {
                Cx = cx; Cy = cy; W = w; H = h;
                Score = score; CocoClass = cocoClass;
                Suppressed = false;
            }

            public Candidate AsSuppressed()
            {
                var c = this;
                c.Suppressed = true;
                return c;
            }
        }
    }
}
