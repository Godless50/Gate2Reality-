using UnityEngine;

namespace Gate2Reality.SceneTwo
{
    /// <summary>Тип реальной поверхности, на которой живёт эхо-зона.</summary>
    public enum EchoSurface : byte { Wall = 0, Table = 1, Floor = 2 }

    /// <summary>
    /// Маркер эхо-зоны на якоре. Чистые данные: порядковый номер, поверхность,
    /// и нормаль (для стен — куда «смотрит» будущий стенсил-портал Step 3).
    /// Никакой логики — поведение зонами управляется из графа и режиссёра.
    /// </summary>
    public sealed class EchoZone : MonoBehaviour
    {
        public int Index { get; private set; }
        public EchoSurface Surface { get; private set; }
        /// <summary>Мировая нормаль поверхности (у стены — в комнату).</summary>
        public Vector3 SurfaceNormal { get; private set; }

        public void Init(int index, EchoSurface surface, Vector3 normal)
        {
            Index = index;
            Surface = surface;
            SurfaceNormal = normal;
        }
    }
}
