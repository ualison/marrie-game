using System;
using Godot;
using MarrieGame.Core.Models;
using MarrieGame.Core.Enums;
using MarrieGame.Systems.LifePools;

namespace MarrieGame.Systems.LifePools
{
    // Coordinates MonsterLifePool and GhostLifePool for a pair
    public class SeparateLifePool
    {
        public int MonsterRedHearts => _monsterPool?.MonsterRedHearts ?? 0;
        public int MaxMonsterRed => _monsterPool?.MaxMonsterRed ?? 3;
        public int GhostPurpleHearts => _ghostPool?.GhostPurpleHearts ?? 0;
        public int MaxGhostPurple => _ghostPool?.MaxGhostPurple ?? 3;

        private readonly EnemyPair _pair;
        private readonly EnemyManager _manager;

        private readonly MonsterLifePool _monsterPool;
        private readonly GhostLifePool _ghostPool;

        public event Action OnLifeChanged;

        public SeparateLifePool(EnemyPair pair, EnemyManager manager)
        {
            _pair = pair;
            _manager = manager;

            _monsterPool = pair.Monster != null ? new MonsterLifePool(pair.Monster) : null;
            _ghostPool = pair.Ghost != null ? new GhostLifePool(pair.Ghost) : null;
        }

        public void Initialize()
        {
            _monsterPool?.Initialize();
            _ghostPool?.Initialize();

            if (_monsterPool != null)
                _monsterPool.LifeChanged += (_,__,___) => OnLifeChanged?.Invoke();
            if (_ghostPool != null)
                _ghostPool.LifeChanged += (_,__,___) => OnLifeChanged?.Invoke();

            OnLifeChanged?.Invoke();
        }

        public bool HandleMonsterDamage(int damage, Reality reality)
        {
            if (reality != Reality.CurrentReality) return false;
            if (_monsterPool == null) return false;

            bool res = _monsterPool.HandleDamage(damage);
            if (res && _monsterPool.MonsterRedHearts <= 0 && !_pair.IsHidden)
            {
                _manager?.HandleMonsterRedDepleted(_pair);
            }

            return res;
        }

        public bool HandleGhostDamage(int damage, Reality reality)
        {
            if (reality != Reality.PastReality) return false;
            if (_ghostPool == null) return false;

            bool res = _ghostPool.HandleDamage(damage);
            if (res && _ghostPool.GhostPurpleHearts <= 0 && !_pair.IsHidden)
            {
                _manager?.HandleGhostDeath(_pair);
            }

            return res;
        }

        public void ResetBothToFull()
        {
            Log.Debug($"SeparateLifePool.ResetBothToFull for pair {_pair.PairId}", "SeparateLifePool");
            _monsterPool?.ResetToFull();
            _ghostPool?.ResetToFull();
            OnLifeChanged?.Invoke();
        }

        public void ResetMonsterRedToFull()
        {
            Log.Debug($"SeparateLifePool.ResetMonsterRedToFull for pair {_pair.PairId}", "SeparateLifePool");
            _monsterPool?.ResetToFull();
            OnLifeChanged?.Invoke();
        }

        public void Cleanup()
        {
            _monsterPool?.Cleanup();
            _ghostPool?.Cleanup();
        }

        public string GetStatus()
        {
            if (GhostPurpleHearts <= 0) return "GHOST_DEAD";
            if (MonsterRedHearts <= 0) return "MONSTER_HIDDEN";
            if (_pair.IsHidden) return "PAIR_HIDDEN";
            return "ALIVE";
        }

    // True when this pair has an actual ghost entity to track
    public bool HasGhost => _pair.Ghost != null;

        private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
    }
}
