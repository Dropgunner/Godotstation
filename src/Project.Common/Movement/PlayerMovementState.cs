using System;

namespace Project.Common.Movement
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

        public float CurrentSpeed
        {
            get
            {
                if (IsBuckled || IsSlipping) return 0f;

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
