using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// Applies the difficulty picked in the main menu whenever a level with a player loads:
    /// Invincible makes the player unkillable for testing, Easy and Hard scale the damage the player
    /// takes, and Hard also toughens every enemy.
    /// </summary>
    public static class DifficultyDirector
    {
        // Domain reload is disabled, so unhook before hooking to avoid a second handler each play session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // Runs after the scene's Awake calls but before Start, so Health.Start picks up the new max health.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var player = Object.FindAnyObjectByType<PlayerCharacterController>();
            if (!player) return;

            var playerHealth = player.GetComponent<Health>();
            var playerActor = player.GetComponent<Actor>();
            if (playerHealth)
            {
                playerHealth.Invincible = GameSettings.Difficulty == GameDifficulty.Invincible;
                playerHealth.DamageTakenMultiplier = GameSettings.PlayerDamageTakenMultiplier;
            }

            float enemyHealth = GameSettings.EnemyHealthMultiplier;
            if (Mathf.Approximately(enemyHealth, 1f) || !playerActor) return;
            foreach (var actor in Object.FindObjectsByType<Actor>(FindObjectsSortMode.None))
            {
                if (actor.Affiliation == playerActor.Affiliation) continue;
                var health = actor.GetComponent<Health>();
                if (health) health.MaxHealth *= enemyHealth;
            }
        }
    }
}
