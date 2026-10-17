using Unity.FPS.Game;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.FPS.Gameplay
{
    /// <summary>
    /// The emergency officer's sidearm locker in the tutorial quarters. Holding Interact on it takes the gun off its
    /// rack: the rack's display model disappears and the weapon is added to the player and drawn.
    /// </summary>
    public class WeaponLocker : KeycardInteractable
    {
        [Tooltip("The weapon handed to the player.")]
        public WeaponController Weapon;
        [Tooltip("The gun shown on the rack; hidden once taken.")]
        public GameObject DisplayModel;
        [Min(0f)] public float Hold = .8f;

        [Header("Wording")]
        public string ActionText = "TAKE SIDEARM";
        public string DetailText = "OFFICER'S BLASTER - EMERGENCY ISSUE";

        [Header("Status")]
        public Renderer[] Lamps;
        public Light LampLight;
        public Color ReadyColor = new Color(.3f, .75f, 1f);
        public Color EmptyColor = new Color(.15f, 1f, .35f);

        [Header("Sound")]
        public AudioClip TakeSfx;

        public bool IsTaken { get; private set; }
        public UnityAction OnTaken;

        public override bool IsAvailable => !IsTaken;
        public override float HoldDuration => Hold;

        public override string PromptAction(KeycardInteractor user) => ActionText;
        public override string PromptDetail(KeycardInteractor user) => DetailText;

        public override void Interact(KeycardInteractor user)
        {
            if (IsTaken || !Weapon) return;
            var weapons = user.GetComponent<PlayerWeaponsManager>();
            if (!weapons) return;

            IsTaken = true;
            if (DisplayModel) DisplayModel.SetActive(false);
            PlayAt(TakeSfx, FocusPosition);
            if (!weapons.HasWeapon(Weapon)) weapons.AddWeapon(Weapon);
            if (!weapons.GetActiveWeapon()) weapons.SwitchWeapon(true);
            OnTaken?.Invoke();
        }

        void Update()
        {
            if (IsTaken) StatusLamps.Set(Lamps, LampLight, EmptyColor, 2.5f);
            else StatusLamps.Set(Lamps, LampLight, ReadyColor, 3.5f * StatusLamps.Pulse(1.4f));
        }
    }
}
