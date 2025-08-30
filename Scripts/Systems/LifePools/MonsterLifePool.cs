using System;
using Godot;

namespace MarrieGame.Systems.LifePools
{
    // Handles Monster (current reality) red-heart life only
    public class MonsterLifePool
    {
        public int MonsterRedHearts { get; private set; }
        public int MaxMonsterRed { get; private set; } = 3;

        private Monster _monster;
        private bool _syncInProgress = false;

        public event Action<int,int,int> LifeChanged; // currentLife, maxLife, purpleHearts (always 0 here)

        public MonsterLifePool(Monster monster)
        {
            _monster = monster;
        }

        public void Initialize()
        {
            if (!IsInstanceValid(_monster)) return;

            var monsterLife = _monster.GetLifeSystem();
            if (monsterLife == null) return;

            MaxMonsterRed = monsterLife.MaxLife;
            MonsterRedHearts = monsterLife.CurrentRedHearts;

            // Ensure monster never has purple hearts configured or visible
            monsterLife.PurpleHearts = 0;
            monsterLife.SetHearts(MonsterRedHearts, 0);

            monsterLife.LifeChanged += OnMonsterLifeChanged;
            Log.Debug($"MonsterLifePool initialized: R={MonsterRedHearts}/{MaxMonsterRed}", "MonsterLifePool");

            LifeChanged?.Invoke(MonsterRedHearts, MaxMonsterRed, 0);
        }

        public bool HandleDamage(int damage)
        {
            if (!IsInstanceValid(_monster)) return false;
            int actualDamage = Math.Min(damage, MonsterRedHearts);
            if (actualDamage <= 0) return false;

            MonsterRedHearts -= actualDamage;
            SyncToMonster();
            LifeChanged?.Invoke(MonsterRedHearts, MaxMonsterRed, 0);
            return true;
        }

        private void OnMonsterLifeChanged(int currentLife, int maxLife, int purpleHearts)
        {
            if (_syncInProgress) return;

            MonsterRedHearts = Math.Max(0, currentLife);
            LifeChanged?.Invoke(MonsterRedHearts, MaxMonsterRed, 0);
        }

        public void ResetToFull()
        {
            MonsterRedHearts = MaxMonsterRed;
            SyncToMonster();
            LifeChanged?.Invoke(MonsterRedHearts, MaxMonsterRed, 0);
        }

        private void SyncToMonster()
        {
            if (!IsInstanceValid(_monster)) return;
            var monsterLife = _monster.GetLifeSystem();
            if (monsterLife == null) return;

            _syncInProgress = true;
            try
            {
                monsterLife.SetHearts(MonsterRedHearts, 0);
                Log.Debug($"MonsterLifePool sync: R={MonsterRedHearts}", "MonsterLifePool");
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
                if (!IsInstanceValid(_monster)) return;
                var monsterLife = _monster.GetLifeSystem();
                if (monsterLife != null)
                {
                    monsterLife.LifeChanged -= OnMonsterLifeChanged;
                }
            }
            catch { }
        }

        private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
    }
}
