using System;

namespace Project.Common.Movement
{
    public struct MoveIntent
    {
        public int DeltaX;
        public int DeltaY;
        public double Timestamp;
    }

    public class MovementProcessor
    {
        public CollisionMap Map { get; }

        public MovementProcessor(CollisionMap map)
        {
            Map = map;
        }

        public bool TryMove(PlayerMovementState state, ref float posX, ref float posY, MoveIntent intent, float deltaTime)
        {
            if (state.IsBuckled || state.IsCuffed || state.IsSlipping)
            {
                return false;
            }

            float speed = state.CurrentSpeed;
            float moveDist = speed * deltaTime;

            // Normalize diagonal movement (diagonal penalty)
            float length = (float)Math.Sqrt(intent.DeltaX * intent.DeltaX + intent.DeltaY * intent.DeltaY);
            if (length == 0) return false;

            float dirX = intent.DeltaX / length;
            float dirY = intent.DeltaY / length;

            float newX = posX + dirX * moveDist;
            float newY = posY + dirY * moveDist;

            // Check collision at destination tile (rounding to nearest int)
            int targetTileX = (int)Math.Round(newX);
            int targetTileY = (int)Math.Round(newY);

            if (Map.IsPassable(targetTileX, targetTileY, state))
            {
                posX = newX;
                posY = newY;
                return true;
            }

            // Sliding along walls (if diagonal fails, try cardinal)
            if (Math.Abs(intent.DeltaX) > 0 && Math.Abs(intent.DeltaY) > 0)
            {
                int targetXOnly = (int)Math.Round(posX + dirX * moveDist);
                int targetYOnly = (int)Math.Round(posY + dirY * moveDist);

                bool canMoveX = Map.IsPassable(targetXOnly, (int)Math.Round(posY), state);
                bool canMoveY = Map.IsPassable((int)Math.Round(posX), targetYOnly, state);

                if (canMoveX && !canMoveY)
                {
                    posX += dirX * moveDist;
                    return true;
                }
                else if (canMoveY && !canMoveX)
                {
                    posY += dirY * moveDist;
                    return true;
                }
            }

            return false;
        }
    }
}
