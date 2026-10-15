using System;

namespace Godotstation.Engine.Movement
{
    public struct MoveIntent
    {
        public int DeltaX;
        public int DeltaY;
        public double Timestamp;
    }

    /// <summary>
    /// Pure, deterministic, engine-agnostic movement resolver. Ported from Unitystation's
    /// <c>MovementSynchronisation</c> / <c>UniversalObjectPhysics</c> (US13). Handles:
    ///   * Input-driven tile movement with wall collision + diagonal wall-sliding.
    ///   * Object pushing (crates/carts/players), including chain-pushing, mirroring
    ///     <c>UniversalObjectPhysics.PushPull.ForceTilePush</c>.
    ///   * Momentum / Newtonian sliding on ice &amp; in space, with friction decay mirroring
    ///     <c>UniversalObjectPhysics.AppliedFriction</c>.
    /// No Godot, no UnityEngine — only <see cref="System.Math"/>.
    /// </summary>
    public class MovementProcessor
    {
        // Friction coefficients (magnitude loss per second) from UniversalObjectPhysics.
        public const float DEFAULT_FRICTION = 15f;      // DEFAULT_Friction: grippy floor / stops fast
        public const float SLIDE_FRICTION = 9f;         // DEFAULT_SLIDE_FRICTION: ice / slides far

        // Below this squared-magnitude a body is considered stationary.
        private const float MomentumEpsilon = 1e-4f;
        private const float PositionEpsilon = 1e-6f;

        // Guard against pathological / circular push chains.
        private const int MaxPushChain = 32;

        public CollisionMap Map { get; }

        public MovementProcessor(CollisionMap map)
        {
            Map = map;
        }

        // ------------------------------------------------------------------
        // Input-driven movement
        // ------------------------------------------------------------------

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

            int curX = (int)Math.Round(posX);
            int curY = (int)Math.Round(posY);
            int stepX = Math.Sign(intent.DeltaX);
            int stepY = Math.Sign(intent.DeltaY);
            bool diagonal = stepX != 0 && stepY != 0;

            // Collision is resolved against the ADJACENT tile in the movement direction
            // (Unitystation is tile-stepping), not the rounded destination. This prevents
            // tunnelling through a wall when moveDist overshoots a tile.
            if (CanEnterTile(state, curX, curY, stepX, stepY))
            {
                DoEnterTile(state, curX, curY, stepX, stepY);
                posX += dirX * moveDist;
                posY += dirY * moveDist;
                ImpartMomentumIfSlippery(state, posX, posY, dirX, dirY, speed);
                return true;
            }

            // Diagonal blocked -> slide along whichever single cardinal axis is open (Rule 3).
            if (diagonal)
            {
                bool canX = CanEnterTile(state, curX, curY, stepX, 0);
                bool canY = CanEnterTile(state, curX, curY, 0, stepY);

                if (canX && !canY)
                {
                    DoEnterTile(state, curX, curY, stepX, 0);
                    posX += dirX * moveDist;
                    ImpartMomentumIfSlippery(state, posX, posY, dirX, 0f, speed);
                    return true;
                }
                if (canY && !canX)
                {
                    DoEnterTile(state, curX, curY, 0, stepY);
                    posY += dirY * moveDist;
                    ImpartMomentumIfSlippery(state, posX, posY, 0f, dirY, speed);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// While a body is standing on / moving over slippery ground, its self-propelled velocity
        /// is retained as momentum so it keeps gliding once input stops (see Rule 5).
        /// </summary>
        private void ImpartMomentumIfSlippery(PlayerMovementState state, float posX, float posY, float dirX, float dirY, float speed)
        {
            int tileX = (int)Math.Round(posX);
            int tileY = (int)Math.Round(posY);
            if (Map.IsSlippery(tileX, tileY))
            {
                state.SetMomentum(dirX * speed, dirY * speed);
            }
        }

        // ------------------------------------------------------------------
        // Momentum / Newtonian sliding
        // ------------------------------------------------------------------

        /// <summary>
        /// Advance a body by its current momentum for <paramref name="deltaTime"/> seconds, resolving
        /// collisions (walls stop it; pushable objects get shoved) and then applying friction decay.
        /// Returns true while the body is still sliding.
        /// Mirrors the newtonian branch of UniversalObjectPhysics' flying/friction update.
        /// </summary>
        public bool ApplyMomentum(PlayerMovementState state, ref float posX, ref float posY, float deltaTime)
        {
            if (state.MomentumSquared < MomentumEpsilon)
            {
                state.SetMomentum(0f, 0f);
                return false;
            }

            // Resolve each axis independently (as with wall-sliding), stepping tile-by-tile so we
            // never tunnel through a wall regardless of how large the displacement is this tick.
            ResolveSlideAxis(state, ref posX, ref posY, state.MomentumX * deltaTime, isXAxis: true);
            ResolveSlideAxis(state, ref posX, ref posY, state.MomentumY * deltaTime, isXAxis: false);

            // Friction: ice (slippery) decelerates gently, grippy floor kills momentum quickly.
            int tileX = (int)Math.Round(posX);
            int tileY = (int)Math.Round(posY);
            float friction = Map.IsSlippery(tileX, tileY) ? SLIDE_FRICTION : DEFAULT_FRICTION;
            ApplyFriction(state, friction, deltaTime);

            return state.MomentumSquared >= MomentumEpsilon;
        }

        private void ResolveSlideAxis(PlayerMovementState state, ref float posX, ref float posY, float distance, bool isXAxis)
        {
            if (Math.Abs(distance) < PositionEpsilon) return;

            int step = distance > 0 ? 1 : -1;
            float remaining = Math.Abs(distance);

            while (remaining > PositionEpsilon)
            {
                int curX = (int)Math.Round(posX);
                int curY = (int)Math.Round(posY);
                float pos = isXAxis ? posX : posY;
                int curTile = isXAxis ? curX : curY;

                // Distance from the body's centre to the centre of the next tile along this axis.
                float distToNextCentre = Math.Abs((curTile + step) - pos);
                if (distToNextCentre < PositionEpsilon) distToNextCentre = 1f;

                if (remaining < distToNextCentre)
                {
                    // Stays within the current tile band — no boundary to cross.
                    if (isXAxis) posX += step * remaining; else posY += step * remaining;
                    return;
                }

                // We would cross into the next tile: check we're allowed to enter it (pushing if needed).
                int enterStepX = isXAxis ? step : 0;
                int enterStepY = isXAxis ? 0 : step;
                if (CanEnterTile(state, curX, curY, enterStepX, enterStepY))
                {
                    DoEnterTile(state, curX, curY, enterStepX, enterStepY);
                    if (isXAxis) posX += step * distToNextCentre; else posY += step * distToNextCentre;
                    remaining -= distToNextCentre;
                }
                else
                {
                    // Hit a wall / immovable object: halt and kill momentum on this axis.
                    if (isXAxis) state.MomentumX = 0f; else state.MomentumY = 0f;
                    return;
                }
            }
        }

        /// <summary>Port of UniversalObjectPhysics.AppliedFriction.</summary>
        private static void ApplyFriction(PlayerMovementState state, float frictionCoefficient, float deltaTime)
        {
            float speedLoss = frictionCoefficient * deltaTime;
            float oldMagnitude = (float)Math.Sqrt(state.MomentumSquared);
            if (oldMagnitude <= 0f) return;

            float newMagnitude = oldMagnitude - speedLoss;
            if (newMagnitude <= 0f)
            {
                state.SetMomentum(0f, 0f);
            }
            else
            {
                float scale = newMagnitude / oldMagnitude;
                state.SetMomentum(state.MomentumX * scale, state.MomentumY * scale);
            }
        }

        // ------------------------------------------------------------------
        // Pushing
        // ------------------------------------------------------------------

        /// <summary>
        /// Attempt to push the object occupying (objX, objY) one tile in the (dirX, dirY) direction.
        /// Chain-pushes any pushable objects ahead of it. Atomic: nothing moves unless the whole
        /// chain can move. Returns true if the push succeeded.
        /// </summary>
        public bool Push(int objX, int objY, int dirX, int dirY, PlayerMovementState state)
        {
            var obj = Map.GetObject(objX, objY);
            if (obj == null || !obj.IsPushable) return false;
            if (dirX == 0 && dirY == 0) return false;

            if (!TryPushChain(objX, objY, dirX, dirY, state, dryRun: true, depth: 0)) return false;
            TryPushChain(objX, objY, dirX, dirY, state, dryRun: false, depth: 0);
            return true;
        }

        /// <summary>
        /// Recursively determine (dryRun) or perform (dryRun == false) pushing the object at
        /// (objX, objY) into (objX+dirX, objY+dirY), clearing pushable objects ahead of it first.
        /// </summary>
        private bool TryPushChain(int objX, int objY, int dirX, int dirY, PlayerMovementState state, bool dryRun, int depth)
        {
            if (depth > MaxPushChain) return false;

            int tx = objX + dirX;
            int ty = objY + dirY;

            // A wall / windoor beyond blocks the push.
            if (!Map.IsPassable(tx, ty, state)) return false;

            var next = Map.GetObject(tx, ty);
            if (next != null)
            {
                if (!next.IsPushable) return false;
                if (!TryPushChain(tx, ty, dirX, dirY, state, dryRun, depth + 1)) return false;
            }

            if (!dryRun)
            {
                var self = Map.GetObject(objX, objY);
                if (self != null)
                {
                    Map.MoveObject(self, tx, ty);
                }
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Tile entry helpers (shared by input movement, sliding, and pushing)
        // ------------------------------------------------------------------

        /// <summary>
        /// Can a body currently at (curX, curY) enter the adjacent tile (curX+stepX, curY+stepY)?
        /// A wall or immovable object blocks; a pushable object is enterable iff it (and everything
        /// chained behind it) can be shoved one tile further in the same direction. Pure — no mutation.
        /// </summary>
        private bool CanEnterTile(PlayerMovementState state, int curX, int curY, int stepX, int stepY)
        {
            int tx = curX + stepX;
            int ty = curY + stepY;

            if (!Map.IsPassable(tx, ty, state)) return false; // wall

            var obj = Map.GetObject(tx, ty);
            if (obj != null)
            {
                if (!obj.IsPushable) return false;
                return TryPushChain(tx, ty, stepX, stepY, state, dryRun: true, depth: 0);
            }
            return true;
        }

        /// <summary>
        /// Commit entry into the adjacent tile: if a pushable object is there, shove it (and the
        /// chain behind it). Caller must have confirmed <see cref="CanEnterTile"/> first.
        /// </summary>
        private void DoEnterTile(PlayerMovementState state, int curX, int curY, int stepX, int stepY)
        {
            int tx = curX + stepX;
            int ty = curY + stepY;
            var obj = Map.GetObject(tx, ty);
            if (obj != null && obj.IsPushable)
            {
                TryPushChain(tx, ty, stepX, stepY, state, dryRun: false, depth: 0);
            }
        }
    }
}
