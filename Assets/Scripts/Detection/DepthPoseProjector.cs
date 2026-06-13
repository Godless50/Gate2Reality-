using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Gate2Reality.Detection
{
    public class DepthPoseProjector : MonoBehaviour
    {
        [SerializeField] private Camera arCamera;
        [SerializeField] private AROcclusionManager occlusionManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private float fallbackDepth = 1.5f;
        [SerializeField] private float planeRaycastDistance = 10f;

        // 3-level fallback: ARCore Depth API → plane raycast → fixed approximation
        public bool TryProjectToWorld(Vector2 screenUv, float imageWidth, out Pose worldPose, out float confidence)
        {
            worldPose = default;
            confidence = 0f;

            Vector2 screenPos = new Vector2(screenUv.x * Screen.width, screenUv.y * Screen.height);

            // Level 1: ARCore environment depth
            if (TryDepthApiProject(screenPos, out worldPose))
            {
                confidence = 1f;
                return true;
            }

            // Level 2: ARPlane raycast
            if (TryPlaneRaycast(screenPos, out worldPose))
            {
                confidence = 0.75f;
                return true;
            }

            // Level 3: fixed-depth approximation
            Ray ray = arCamera.ScreenPointToRay(screenPos);
            worldPose = new Pose(ray.GetPoint(fallbackDepth), Quaternion.LookRotation(-ray.direction));
            confidence = 0.3f;
            return true;
        }

        private bool TryDepthApiProject(Vector2 screenPos, out Pose pose)
        {
            pose = default;
            if (occlusionManager == null) return false;

            var depthTex = occlusionManager.environmentDepthTexture;
            if (depthTex == null) return false;

            float u = screenPos.x / Screen.width;
            float v = screenPos.y / Screen.height;
            // Sample depth (ARCore depth is in millimetres as uint16 encoded in RG channels)
            // We treat this as a valid hit if depth > 0
            Ray ray = arCamera.ScreenPointToRay(screenPos);
            float depthM = SampleDepthMetres(depthTex, u, v);
            if (depthM <= 0f) return false;

            pose = new Pose(ray.GetPoint(depthM), Quaternion.LookRotation(-ray.direction));
            return true;
        }

        private bool TryPlaneRaycast(Vector2 screenPos, out Pose pose)
        {
            pose = default;
            if (planeManager == null) return false;

            Ray ray = arCamera.ScreenPointToRay(screenPos);
            foreach (var plane in planeManager.trackables)
            {
                var planeTransform = plane.transform;
                var planeNormal = planeTransform.up;
                float denom = Vector3.Dot(planeNormal, ray.direction);
                if (Mathf.Abs(denom) < 1e-4f) continue;

                float t = Vector3.Dot(planeNormal, planeTransform.position - ray.origin) / denom;
                if (t < 0f || t > planeRaycastDistance) continue;

                Vector3 hit = ray.GetPoint(t);
                pose = new Pose(hit, Quaternion.LookRotation(-planeNormal, Vector3.up));
                return true;
            }
            return false;
        }

        private static float SampleDepthMetres(Texture2D depthTex, float u, float v)
        {
            if (depthTex == null) return 0f;
            Color c = depthTex.GetPixelBilinear(u, v);
            // Decode packed uint16 from RG (ARCore convention)
            int raw = (int)(c.r * 255f) | ((int)(c.g * 255f) << 8);
            return raw * 0.001f;
        }
    }
}
