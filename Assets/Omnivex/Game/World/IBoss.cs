using UnityEngine;

namespace OMX.Game
{
    /// <summary>What the HUD, the run loop and the player's weapons need from any stage boss.</summary>
    public interface IBoss
    {
        bool Active { get; }
        bool Exposed { get; }
        int Phase { get; }
        string Title { get; }
        float Hp { get; }
        float MaxHp { get; }
        Vector3 Position { get; }
        float Radius { get; }
        void Damage(float amount, bool bite = false);
        bool HitTest(Vector3 p, float r, float dmg);
        Vector3? AimPoint(Vector3 from, float range);
        void Despawn();
    }
}
