using System;
using System.Collections;
using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The ship's core hanging over the Boss Hall: an unstable heart inside three layers of armour, outermost
    /// first, open at the bottom where the reactor's beam feeds it. Each layer is blown off whole by an overcharged
    /// reactor console (<see cref="BlowOffActiveLayer"/>); gunfire only rings off it, unless
    /// <see cref="ShotsBreakArmour"/> brings back the older shoot-it-off fight (shots then count only on the
    /// outermost layer, its most-stressed pieces come away as it wears down and the rest go when it fails). With
    /// every layer gone the heart is exposed for the final execution, which drives Overload, Collapse and Burst.
    /// </summary>
    public class GiantCore : MonoBehaviour
    {
        [Serializable]
        public class ArmourLayer
        {
            public string Name = "Steel plating";
            public ArmourStyle Style;
            [Min(1f)] public float MaxHealth = 2000f;
            public ArmourPiece[] Pieces;
            [Tooltip("Share of the layer's pieces that come away as it wears down; the rest go when it fails.")]
            [Range(0f, 1f)] public float ShedBeforeBreak = .6f;
            public AudioClip PieceSfx;
            public AudioClip BreakSfx;

            [NonSerialized] public float Health;
        }

        [Tooltip("Outermost first.")]
        public ArmourLayer[] Layers;
        [Tooltip("Gunfire wears the armour down (the older fight). Off: only overcharged consoles remove layers.")]
        public bool ShotsBreakArmour;
        [Tooltip("How hard a blown-off layer's pieces are flung.")]
        [Min(1f)] public float BlowOffForce = 3.2f;

        [Header("Heart")]
        public Renderer Heart;
        public Light HeartLight;
        [Tooltip("Instability of the heart while fully armoured, rising to 1 as the armour comes off.")]
        [Range(0f, 1f)] public float ArmouredInstability = .25f;

        [Header("Ceramic Shards")]
        public Mesh[] ShardMeshes;
        public Material ShardMaterial;

        [Header("Sound")]
        public AudioClip DeflectSfx;
        [Range(0f, 1f)] public float SfxVolume = 1f;

        /// <summary>Hits count only once the fight is on (the player has entered the hall).</summary>
        public bool IsArmed { get; set; }
        public int ActiveLayer { get; private set; }
        public bool IsExposed => Layers == null || ActiveLayer >= Layers.Length;
        public string ActiveLayerName => IsExposed ? "Core" : Layers[ActiveLayer].Name;
        public float ActiveLayerRatio => IsExposed ? 0f : Mathf.Clamp01(Layers[ActiveLayer].Health / Layers[ActiveLayer].MaxHealth);
        public Vector3 HeartPosition => Heart ? Heart.bounds.center : transform.position;

        /// <summary>0..1: the final execution pushing the heart past its limit.</summary>
        public float Overload { get; set; }
        /// <summary>0..1: the heart caving in just before it bursts.</summary>
        public float Collapse { get; set; }
        /// <summary>Scale the bursting heart swells to (1 = rest).</summary>
        public float Burst { get; set; } = 1f;

        public UnityAction<int> OnLayerBroken;
        public UnityAction OnExposed;

        static readonly int k_CoreTime = Shader.PropertyToID("_CoreTime");
        static readonly int k_Instability = Shader.PropertyToID("_Instability");
        static readonly int k_Surge = Shader.PropertyToID("_Surge");
        static readonly int k_Collapse = Shader.PropertyToID("_Collapse");
        static readonly int k_Intensity = Shader.PropertyToID("_Intensity");

        MaterialPropertyBlock m_HeartBlock;
        Vector3 m_HeartScale;
        float m_BaseIntensity = 2.2f;
        float m_LightIntensity;
        float m_CoreTime;
        float m_Surge;
        float m_NextDeflectSfx;
        GameObject m_Player;

        void Awake()
        {
            m_HeartBlock = new MaterialPropertyBlock();
            if (Heart)
            {
                m_HeartScale = Heart.transform.localScale;
                if (Heart.sharedMaterial && Heart.sharedMaterial.HasProperty(k_Intensity))
                    m_BaseIntensity = Heart.sharedMaterial.GetFloat(k_Intensity);
            }
            if (HeartLight) m_LightIntensity = HeartLight.intensity;

            for (int i = 0; Layers != null && i < Layers.Length; i++)
            {
                var layer = Layers[i];
                layer.Health = layer.MaxHealth;
                int index = i;
                foreach (var piece in layer.Pieces)
                {
                    if (!piece) continue;
                    var hitPiece = piece;
                    hitPiece.Health.OnDamaged += (damage, source) => OnPieceHit(hitPiece, index, damage, source);
                }
            }
        }

        void Start()
        {
            var player = FindAnyObjectByType<PlayerCharacterController>();
            if (player) m_Player = player.gameObject;
        }

        void OnPieceHit(ArmourPiece piece, int layerIndex, float damage, GameObject source)
        {
            if (piece.Detached || !IsArmed || !IsFromPlayer(source)) return;
            if (!ShotsBreakArmour || layerIndex != ActiveLayer)
            {
                if (Time.time >= m_NextDeflectSfx)
                {
                    m_NextDeflectSfx = Time.time + .15f;
                    PlayAt(DeflectSfx, piece.transform.position, .5f);
                }
                return;
            }

            var layer = Layers[layerIndex];
            layer.Health = Mathf.Max(0f, layer.Health - damage);
            piece.Stress += damage;
            ShowWear(layer);
            if (layer.Health <= 0f) BreakLayer(layerIndex);
            else ShedWornPieces(layer);
        }

        bool IsFromPlayer(GameObject source)
        {
            if (!source) return false;
            if (m_Player) return source == m_Player || source.transform.IsChildOf(m_Player.transform);
            return source.GetComponentInParent<PlayerCharacterController>();
        }

        // Every piece shows the layer's general wear; the ones the player has hammered show more.
        void ShowWear(ArmourLayer layer)
        {
            float wear = 1f - layer.Health / layer.MaxHealth;
            float perPiece = layer.MaxHealth / Mathf.Max(1, layer.Pieces.Length);
            foreach (var piece in layer.Pieces)
                if (piece && !piece.Detached)
                    piece.SetDamage(Mathf.Max(wear * .6f, piece.Stress / (perPiece * 1.2f)));
        }

        void ShedWornPieces(ArmourLayer layer)
        {
            float wear = 1f - layer.Health / layer.MaxHealth;
            int target = Mathf.FloorToInt(wear * layer.ShedBeforeBreak * layer.Pieces.Length);
            while (CountDetached(layer) < target)
            {
                ArmourPiece worst = null;
                foreach (var piece in layer.Pieces)
                    if (piece && !piece.Detached && (!worst || piece.Stress > worst.Stress))
                        worst = piece;
                if (!worst) break;
                Detach(layer, worst);
            }
        }

        static int CountDetached(ArmourLayer layer)
        {
            int count = 0;
            foreach (var piece in layer.Pieces)
                if (!piece || piece.Detached) count++;
            return count;
        }

        void Detach(ArmourLayer layer, ArmourPiece piece)
        {
            PlayAt(layer.PieceSfx, piece.transform.position, .8f);
            piece.Detach(ShardMeshes, ShardMaterial);
        }

        /// <summary>The reactor's overcharge blasts the outermost remaining layer off the core, all at once.</summary>
        public void BlowOffActiveLayer()
        {
            if (IsExposed) return;
            int index = ActiveLayer;
            var layer = Layers[index];
            layer.Health = 0f;
            PlayAt(layer.BreakSfx, HeartPosition);
            StartCoroutine(BlowOff(layer));
            ActiveLayer = index + 1;
            m_Surge = 1f;
            OnLayerBroken?.Invoke(index);
            if (IsExposed) OnExposed?.Invoke();
        }

        IEnumerator BlowOff(ArmourLayer layer)
        {
            foreach (var piece in layer.Pieces)
            {
                if (!piece || piece.Detached) continue;
                piece.Detach(ShardMeshes, ShardMaterial, BlowOffForce);
                if (UnityEngine.Random.value < .3f) yield return null;
            }
        }

        void BreakLayer(int layerIndex)
        {
            var layer = Layers[layerIndex];
            PlayAt(layer.BreakSfx, HeartPosition);
            StartCoroutine(ShedRemaining(layer));
            ActiveLayer = layerIndex + 1;
            m_Surge = 1f;
            OnLayerBroken?.Invoke(layerIndex);
            if (IsExposed) OnExposed?.Invoke();
        }

        IEnumerator ShedRemaining(ArmourLayer layer)
        {
            foreach (var piece in layer.Pieces)
            {
                if (!piece || piece.Detached) continue;
                piece.Detach(ShardMeshes, ShardMaterial);
                yield return new WaitForSeconds(UnityEngine.Random.Range(.01f, .06f));
            }
        }

        void Update()
        {
            if (!Heart) return;

            float armourLeft = 0f, armourTotal = 0f;
            foreach (var layer in Layers)
            {
                armourLeft += layer.Health;
                armourTotal += layer.MaxHealth;
            }
            float stripped = armourTotal > 0f ? 1f - armourLeft / armourTotal : 1f;

            m_Surge = Mathf.MoveTowards(m_Surge, 0f, Time.deltaTime * .8f);
            m_CoreTime += Time.deltaTime * (1f + 2.5f * Overload);
            float instability = Mathf.Max(Mathf.Lerp(ArmouredInstability, 1f, stripped), Overload);
            float surge = Mathf.Max(m_Surge, Overload * (.6f + .4f * Mathf.Sin(m_CoreTime * 14f)));

            Heart.GetPropertyBlock(m_HeartBlock);
            m_HeartBlock.SetFloat(k_CoreTime, m_CoreTime);
            m_HeartBlock.SetFloat(k_Instability, instability);
            m_HeartBlock.SetFloat(k_Surge, Mathf.Clamp01(surge));
            m_HeartBlock.SetFloat(k_Collapse, Collapse);
            m_HeartBlock.SetFloat(k_Intensity, m_BaseIntensity * (1f + 3f * Overload + 6f * Mathf.Max(0f, Burst - 1f)));
            Heart.SetPropertyBlock(m_HeartBlock);

            float throb = 1f + (.03f + .1f * Overload) * Mathf.Sin(m_CoreTime * (3f + 10f * Overload));
            Heart.transform.localScale = m_HeartScale * (throb * (1f - .75f * Collapse) * Burst);

            if (HeartLight)
                HeartLight.intensity = m_LightIntensity * (1f + 1.5f * stripped + 4f * Overload + 3f * surge) *
                                       Mathf.Max(1f, Burst);
        }

        void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip) AudioSource.PlayClipAtPoint(clip, position, SfxVolume * volume);
        }
    }
}
