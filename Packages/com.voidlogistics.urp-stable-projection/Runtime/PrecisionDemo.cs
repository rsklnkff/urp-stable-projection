using UnityEngine;

namespace StableProjection
{
    [DefaultExecutionOrder(9000)]
    public sealed class PrecisionDemo : MonoBehaviour
    {
        public Camera demoCamera;
        public Transform cameraRig;
        public Transform geometryRoot;
        public PreciseOrthographicCamera preciseCamera;
        public float distanceFromOrigin = 240000;
        bool _animate = true;
        float _time;
        bool _near;
        bool _perspective;
        float _zoom = 4;

        void Update()
        {
            if (_animate)
            {
                _time += Time.unscaledDeltaTime;
            }

            var origin = _near ? Vector3.zero : new Vector3(distanceFromOrigin, 0, -distanceFromOrigin);
            cameraRig.position = origin;
            geometryRoot.position = origin;
            demoCamera.orthographic = !_perspective;
            demoCamera.orthographicSize = _zoom;
            demoCamera.fieldOfView = 40;
            var offset = new Vector3(Mathf.Sin(_time * 0.31f) * 0.04f, 0.5f + Mathf.Sin(_time * 0.4f) * 0.03f, -12);
            demoCamera.transform.localPosition = offset;
            demoCamera.transform.localRotation = Quaternion.LookRotation(-offset, Vector3.up);
            preciseCamera.enabled = !_perspective;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 600, 145), GUI.skin.box);
            GUILayout.Label("Stable Projection for URP — identical local geometry");
            GUILayout.Label("LEFT: conventional world-space vertex projection    RIGHT: installed URP helpers");
            GUILayout.BeginHorizontal();
            _animate = GUILayout.Toggle(_animate, "Move camera");
            _near = GUILayout.Toggle(_near, "At origin");
            _perspective = GUILayout.Toggle(_perspective, "Perspective");
            GUILayout.EndHorizontal();
            GUILayout.Label("Orthographic size: " + _zoom.ToString("F2") + " | coordinate: " + (_near ? "0" : distanceFromOrigin.ToString("F0")));
            _zoom = GUILayout.HorizontalSlider(_zoom, 1.5f, 6);
            GUILayout.Label("Use a native-resolution capture. This is a visual comparison, not a GPU benchmark.");
            GUILayout.EndArea();
        }
    }
}
