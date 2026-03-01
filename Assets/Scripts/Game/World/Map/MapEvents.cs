using Extensions;
using Unity.Cinemachine;
using UnityEngine;

namespace Game.World.Map
{
    public class MapEvents : MonoBehaviour
    {
        [SerializeField] private AudioClip clickSound;
        [SerializeField] private AudioClip hoverSound;

        private MapRenderer _mapRenderer;
        private AudioSource _audioSource;
        private MapPicker _picker;

        [SerializeField] private CinemachineCamera cinemachineCamera;
        [SerializeField] private Transform camTarget;

        public float zoomThreshold = 2f;
        public float minOrthoSize = 2f;
        public float defaultOrthoSize = 4f;

        private float _orthoVel;
        private float _targetOrthoSize;

        private void Start()
        {
            _mapRenderer = GetComponent<MapRenderer>();
            _audioSource = GetComponent<AudioSource>();
            _picker = GetComponent<MapPicker>();

            _picker.provinceClicked.AddListener(OnProvinceClicked);
            _picker.hoverChanged.AddListener(OnProvinceHovered);
            _picker.provinceDoubleClicked.AddListener(OnProvinceDoubleClicked);
            _picker.provinceRightClicked.AddListener(OnProvinceRightClicked);

            _targetOrthoSize = defaultOrthoSize;
        }

        private void OnProvinceRightClicked(Color32 color)
        {
            // Placeholder for right click logic if needed in MapEvents
            // For now, it doesn't do anything specific here, but the event is wired up.
        }

        private void Update()
        {
            cinemachineCamera.Lens.OrthographicSize =
                Mathf.SmoothDamp(
                    cinemachineCamera.Lens.OrthographicSize,
                    _targetOrthoSize,
                    ref _orthoVel,
                    0.35f
                );
        }

        private void OnProvinceDoubleClicked(Color32 color)
        {
            if (color.SameAs(Color.clear))
            {
                camTarget.position = Vector3.zero;
                _targetOrthoSize = defaultOrthoSize;
            }
            else
            {
                var bounds = _mapRenderer.GetProvinceBounds(color);
                camTarget.position = bounds.center;
                FitOrthoToBounds(bounds);
            }
        }

        private void OnProvinceClicked(Color32 color)
        {
            _audioSource.PlayOneShot(clickSound, 1.5f);
        }

        private void OnProvinceHovered(Color32 color)
        {
            if (color == Color.clear) return;
            _audioSource.PlayOneShot(hoverSound, 0.15f);
        }

        private void FitOrthoToBounds(Bounds b)
        {
            // Use the camera that actually renders to get aspect
            var unityCam = Camera.main;
            var aspect = unityCam != null ? unityCam.aspect : (16f / 9f);

            var halfHeight = b.extents.y;
            var halfWidthAsHeight = b.extents.x / aspect;

            var size = Mathf.Max(halfHeight, halfWidthAsHeight) * zoomThreshold;

            // Cinemachine ortho size is half the vertical size in world units
            // cinemachineCamera.Lens.OrthographicSize = Mathf.Max(0.01f, size);
            _targetOrthoSize = Mathf.Max(minOrthoSize, size);
        }
    }
}