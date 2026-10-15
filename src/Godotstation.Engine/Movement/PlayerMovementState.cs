using System;

namespace Godotstation.Engine.Movement
{
    public enum MovementType
    {
        Running,
        Walking,
        Crawling
    }

    public enum PlayerMoveDirection
    {
        Up,
        Up_Right,
        Right,
        Down_Right,
        Down,
        Down_Left,
        Left,
        Up_Left
    }

    public class PlayerMovementState
    {
        public MovementType CurrentType { get; set; } = MovementType.Running;

        // Base speeds per limb (summed across 2 legs)
        // From Limb.cs: walking=1.5, running=3.0, crawling=0.5
        // Total for 2 legs: Walk=3.0, Run=6.0, Crawl=1.0
        public float BaseWalkSpeed { get; set; } = 3.0f;
        public float BaseRunSpeed { get; set; } = 6.0f;
        public float BaseCrawlSpeed { get; set; } = 1.0f;

        public float SpeedModifier { get; set; } = 1.0f; // For magboots, fat, etc.
        public bool IsCuffed { get; set; } = false;
        public bool IsBuckled { get; set; } = false;
        public bool IsSlipping { get; set; } = false;

        // --- Momentum (Newtonian sliding), mirrors UniversalObjectPhysics.newtonianMovement ---
        // Velocity carried independently of input, in tiles/second. Non-zero while sliding on
        // ice / in space or after being pushed/thrown. Decays via friction each physics tick.
        public float MomentumX { get; set; } = 0f;
        public float MomentumY { get; set; } = 0f;

        /// <summary>Squared magnitude of the current momentum vector (tiles/second)^2.</summary>
        public float MomentumSquared => MomentumX * MomentumX + MomentumY * MomentumY;

        public bool IsSliding => MomentumSquared > 0f;

        public void SetMomentum(float x, float y)
        {
            MomentumX = x;
            MomentumY = y;
        }

        public float CurrentSpeed
        {
            get
            {
                // A player who is buckled, slipping, or cuffed cannot self-propel.
                // (See FidelityContract_Movement Rule 2.)
                if (IsBuckled || IsSlipping || IsCuffed) return 0f;

                float speed = CurrentType switch
                {
                    MovementType.Running => BaseRunSpeed,
                    MovementType.Walking => BaseWalkSpeed,
                    MovementType.Crawling => BaseCrawlSpeed,
                    _ => BaseWalkSpeed
                };

                return Math.Max(0.6f, speed * SpeedModifier); // 0.6f is MINIMUM_MOVEMENT_SPEED
            }
        }
    }
}
