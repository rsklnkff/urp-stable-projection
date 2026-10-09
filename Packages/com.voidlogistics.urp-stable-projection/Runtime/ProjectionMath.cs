using UnityEngine;

namespace StableProjection
{
    /// <summary>A double-precision position independent of any gameplay or ECS framework.</summary>
    [System.Serializable]
    public struct DoublePosition
    {
        public double x, y, z;
        public DoublePosition(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public Vector3 ToVector3() => new Vector3((float)x, (float)y, (float)z);
    }

    public static class ProjectionMath
    {
        /// <summary>Compensates the translation rounded into an orthographic camera's float view matrix.</summary>
        public static Matrix4x4 CorrectOrthographic(Matrix4x4 view, Matrix4x4 projection, DoublePosition position, out float depthOffset)
        {
            double x = -((double)view.m00 * position.x + (double)view.m01 * position.y + (double)view.m02 * position.z) - view.m03;
            double y = -((double)view.m10 * position.x + (double)view.m11 * position.y + (double)view.m12 * position.z) - view.m13;
            double z = -((double)view.m20 * position.x + (double)view.m21 * position.y + (double)view.m22 * position.z) - view.m23;
            for (int row = 0; row < 4; row++)
            {
                projection[row, 3] = (float)(projection[row, 3] + (double)projection[row, 0] * x + (double)projection[row, 1] * y + (double)projection[row, 2] * z);
            }

            depthOffset = (float)z;
            return projection;
        }

        /// <summary>Accumulates local rig transforms in doubles. It cannot recover detail already lost in Transform.localPosition.</summary>
        public static DoublePosition AccumulatePosition(Transform source)
        {
            var point = new DoublePosition();
            while (source != null)
            {
                var r = Matrix4x4.Rotate(source.localRotation);
                var scale = source.localScale;
                double x = point.x * scale.x, y = point.y * scale.y, z = point.z * scale.z;
                var t = source.localPosition;
                point = new DoublePosition(r.m00 * x + r.m01 * y + r.m02 * z + t.x,
                    r.m10 * x + r.m11 * y + r.m12 * z + t.y, r.m20 * x + r.m21 * y + r.m22 * z + t.z);
                source = source.parent;
            }

            return point;
        }
    }
}
