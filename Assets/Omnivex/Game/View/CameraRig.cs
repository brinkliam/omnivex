using UnityEngine;

namespace OMX.Game
{
    /// <summary>
    /// Follow camera that zooms with the creature's size, so growth reads as the world shrinking around you.
    /// Trauma-based shake, zoom punches, and a cinematic override for evolutions, the boss and stage transitions.
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public Transform Target;
        public Vector3 TargetVelocity;
        public float SubjectRadius = 0.5f;
        public float SightBonus;
        public bool ShakeEnabled = true;
        public float Pitch = 58f;
        public float Yaw = 0f;

        Vector3 _focus, _focusVel;
        float _dist, _distVel, _trauma, _punch;
        Vector3 _lead;

        // cinematic override
        Vector3 _cinFocus; float _cinDist, _cinWeight, _cinTarget, _cinPitch;

        public void Init()
        {
            Cam = gameObject.AddComponent<Camera>();
            Cam.fieldOfView = 42; Cam.nearClipPlane = 0.2f; Cam.farClipPlane = 600;
            Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = Palette.Abyss;
            gameObject.tag = "MainCamera";
            gameObject.AddComponent<AudioListener>();
            _dist = DesiredDistance();
        }

        float DesiredDistance() => 7.5f + SubjectRadius * 10.5f + SightBonus * 4f;

        public void Shake(float amount) { if (ShakeEnabled) _trauma = Mathf.Min(1, _trauma + amount); }
        public void Punch(float amount) => _punch = Mathf.Clamp(_punch + amount, -0.4f, 0.4f);

        /// <summary>Blend toward a framed shot. weight 1 = fully cinematic; 0 releases back to follow.</summary>
        public void Cinematic(Vector3 focus, float distance, float weight, float pitch = 58f)
        {
            _cinFocus = focus; _cinDist = distance; _cinTarget = weight; _cinPitch = pitch;
        }

        public void SnapToTarget()
        {
            if (Target == null) return;
            _focus = Target.position; _dist = DesiredDistance();
            LateUpdate();
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (Target != null)
            {
                _lead = Vector3.Lerp(_lead, Vector3.ClampMagnitude(TargetVelocity * 0.28f, 3f + SubjectRadius), dt * 2.5f);
                _focus = Vector3.SmoothDamp(_focus, Target.position + _lead, ref _focusVel, 0.18f, Mathf.Infinity, dt);
            }
            _dist = Mathf.SmoothDamp(_dist, DesiredDistance(), ref _distVel, 0.6f, Mathf.Infinity, dt);
            _cinWeight = Mathf.MoveTowards(_cinWeight, _cinTarget, dt * 1.4f);
            float cw = Mathf.SmoothStep(0, 1, _cinWeight);

            var focus = Vector3.Lerp(_focus, _cinFocus, cw);
            float dist = Mathf.Lerp(_dist, _cinDist, cw) * (1 - _punch);
            float pitch = Mathf.Lerp(Pitch, _cinPitch, cw);
            _punch = Mathf.MoveTowards(_punch, 0, dt * 0.9f);

            var rot = Quaternion.Euler(pitch, Yaw, 0);
            var pos = focus - rot * Vector3.forward * dist;

            _trauma = Mathf.Max(0, _trauma - dt * 1.6f);
            float s = _trauma * _trauma;
            if (s > 0)
            {
                float t = Time.unscaledTime * 24;
                pos += (rot * new Vector3(Mathf.PerlinNoise(t, 1) - 0.5f, Mathf.PerlinNoise(2, t) - 0.5f, 0)) * s * (0.8f + dist * 0.04f);
                rot *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, 7) - 0.5f) * 6 * s);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
