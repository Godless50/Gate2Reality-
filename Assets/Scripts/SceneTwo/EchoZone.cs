using UnityEngine;
using Gate2Reality.Narrative;

namespace Gate2Reality.SceneTwo
{
    public enum EchoSurface : byte { Wall, Table, Floor }

    public class EchoZone : MonoBehaviour
    {
        public int Index { get; private set; }
        public EchoSurface Surface { get; private set; }
        public Vector3 SurfaceNormal { get; private set; }

        public void Init(int index, EchoSurface surface, Vector3 surfaceNormal)
        {
            Index = index;
            Surface = surface;
            SurfaceNormal = surfaceNormal;
            narrativeManager?.SetNodeRuntimeTarget(index, transform);
        }

        [SerializeField] private NarrativeManager narrativeManager;
    }
}
