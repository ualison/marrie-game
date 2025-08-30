using System;
using Godot;

namespace MarrieGame.Systems.LifePools
{
    // Handles Ghost (past reality) purple-heart life only
    public class GhostLifePool
    {
        public int GhostPurpleHearts { get; private set; }
        public int MaxGhostPurple { get; private set; } = 3;

        private GhostMonster _ghost;
        private bool _syncInProgress = false;

        public event Action<int,int,int> LifeChanged; // currentLife, maxLife, purpleHearts

        public GhostLifePool(GhostMonster ghost)
        {
            _ghost = ghost;
        }

        public void Initialize()
        {
            if (!IsInstanceValid(_ghost)) return;

            var ghostLife = _ghost.LifeSystem;
            if (ghostLife == null) return;

            MaxGhostPurple = ghostLife.PurpleHearts;

            if (ghostLife.CurrentPurpleHearts <= 0)
            {
                ghostLife.SetHearts(0, MaxGhostPurple);
                GhostPurpleHearts = MaxGhostPurple;
            }
            else
            {
                GhostPurpleHearts = ghostLife.CurrentPurpleHearts;
                ghostLife.SetHearts(0, GhostPurpleHearts);
            }

            ghostLife.LifeChanged += OnGhostLifeChanged;
            Log.Debug($"GhostLifePool initialized: P={GhostPurpleHearts}/{MaxGhostPurple}", "GhostLifePool");

            LifeChanged?.Invoke(0, MaxGhostPurple, GhostPurpleHearts);
        }

        public bool HandleDamage(int damage)
        {
            if (!IsInstanceValid(_ghost)) return false;
            int actualDamage = Math.Min(damage, GhostPurpleHearts);
            if (actualDamage <= 0) return false;

            GhostPurpleHearts -= actualDamage;
            SyncToGhost();
            LifeChanged?.Invoke(0, MaxGhostPurple, GhostPurpleHearts);
            return true;
        }

        private void OnGhostLifeChanged(int currentLife, int maxLife, int purpleHearts)
        {
            if (_syncInProgress) return;

            GhostPurpleHearts = Math.Max(0, purpleHearts);
            LifeChanged?.Invoke(0, MaxGhostPurple, GhostPurpleHearts);
        }

        public void ResetToFull()
        {
            GhostPurpleHearts = MaxGhostPurple;
            SyncToGhost();
            LifeChanged?.Invoke(0, MaxGhostPurple, GhostPurpleHearts);
        }

        private void SyncToGhost()
        {
            if (!IsInstanceValid(_ghost)) return;
            var ghostLife = _ghost.LifeSystem;
            if (ghostLife == null) return;

            _syncInProgress = true;
            try
            {
                ghostLife.SetHearts(0, GhostPurpleHearts);
                Log.Debug($"GhostLifePool sync: P={GhostPurpleHearts}", "GhostLifePool");
            }
            finally
            {
                _syncInProgress = false;
            }
        }

        public void Cleanup()
        {
            try
            {
                if (!IsInstanceValid(_ghost)) return;
                var ghostLife = _ghost.LifeSystem;
                if (ghostLife != null)
                {
                    ghostLife.LifeChanged -= OnGhostLifeChanged;
                }
            }
            catch { }
        }

        private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
    }
}
