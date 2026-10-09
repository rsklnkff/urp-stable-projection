using UnityEngine;
using UnityEngine.Rendering;

namespace StableProjection
{
    /// <summary>Owns the camera's automatic orthographic projection. Do not combine with another custom-projection owner.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(10000)]
    public sealed class PreciseOrthographicCamera : MonoBehaviour
    {
        Camera _camera;
        bool _applied;
        float _baseNear, _baseFar, _appliedNear, _appliedFar;
        int _externalPositionFrame = -1;
        DoublePosition _externalPosition;

        /// <summary>Call each frame after positioning the camera when the game has a more precise authoritative camera position.</summary>
        public void SetPrecisePosition(double x, double y, double z)
        {
            _externalPosition = new DoublePosition(x, y, z);
            _externalPositionFrame = Time.frameCount;
        }

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        void LateUpdate()
        {
            ApplyProjection();
        }

        void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == _camera)
            {
                ApplyProjection();
            }
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RestoreProjection();
            _externalPositionFrame = -1;
        }

        public void ApplyProjection()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }

            RestoreProjection();
            if (_camera == null || !_camera.orthographic || _camera.stereoEnabled)
            {
                return;
            }

            var position = _externalPositionFrame == Time.frameCount ? _externalPosition : ProjectionMath.AccumulatePosition(_camera.transform);
            var projection = ProjectionMath.CorrectOrthographic(_camera.worldToCameraMatrix, _camera.projectionMatrix, position, out var depthOffset);
            _baseNear = _camera.nearClipPlane;
            _baseFar = _camera.farClipPlane;
            _camera.nearClipPlane = _baseNear + depthOffset;
            _camera.farClipPlane = _baseFar + depthOffset;
            _appliedNear = _camera.nearClipPlane;
            _appliedFar = _camera.farClipPlane;
            _camera.projectionMatrix = projection;
            _camera.nonJitteredProjectionMatrix = projection;
            _applied = true;
        }

        public void RestoreProjection()
        {
            if (!_applied || _camera == null)
            {
                return;
            }

            if (_camera.nearClipPlane == _appliedNear)
            {
                _camera.nearClipPlane = _baseNear;
            }

            if (_camera.farClipPlane == _appliedFar)
            {
                _camera.farClipPlane = _baseFar;
            }

            _camera.ResetProjectionMatrix();
            _camera.nonJitteredProjectionMatrix = _camera.projectionMatrix;
            _applied = false;
        }
    }
}
