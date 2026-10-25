using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // A side's leader: its card and its crown. The crown shows the ability's state (gold while it can be used, grey once
    // used, faded out for a passive leader, always in effect and never used, red when cancelled) and, for the player,
    // uses it: it glows and becomes a button only while the leader card is lifted (BattleController.LeaderSelected).
    public class LeaderSlot : MonoBehaviour
    {
        [HideInInspector]
        public BattleController controller;

        public Row type;

        private void Start()
        {
            controller = BattleController.Instance;
        }

        private Button activateButton;
        private bool used;
        private bool cancelled;
        private bool ready = true; // the ability can still be used this round (not used, cancelled or passed)
        private bool lifted;       // the player's leader card is out of its slot: the crown uses it
        private Sprite plainFace;  // the gold crown; the glowing one is UITheme.leaderReady
        private MotionHandle throb;

        private static readonly Color UsedGrey = new Color(0.45f, 0.45f, 0.45f, 0.9f);
        private static readonly Color PassiveFaded = new Color(1f, 1f, 1f, 0.35f); // the gold crown, phased out
        private static readonly Color CancelledRed = new Color32(255, 134, 134, 200);

        private Button ActivateButton
        {
            get
            {
                if (activateButton == null)
                    activateButton = transform.Find("Activate").GetComponent<Button>();
                return activateButton;
            }
        }

        // The opponent's crown only shows the state of their ability: it is not a button for the player
        public Button Button { get { return ActivateButton; } }

        private bool IsPlayers
        {
            get
            {
                if (controller == null)
                    controller = BattleController.Instance;
                return controller != null && transform.IsChildOf(controller.PlayerField.transform);
            }
        }

        private void Awake()
        {
            UITooltip tooltip = ActivateButton.gameObject.AddComponent<UITooltip>();
            tooltip.text = TooltipText;
            UIKit.GrowTouchArea((RectTransform)ActivateButton.transform, new Vector2(100, 100));
        }

        public void UISetPassive()
        {
            if (!IsPlayers)
                ActivateButton.transition = Selectable.Transition.None; // no hover / press look
            Refresh();
        }

        // The ability was used: the crown greys out
        public void MarkUsed()
        {
            used = true;
            Refresh();
        }

        // Cancelled by the opponent's leader (Emhyr, The White Flame): the crown turns red
        public void MarkCancelled()
        {
            cancelled = true;
            Refresh();
        }

        // Whether the ability can still be used this round (a side that passed cannot), every frame
        public void UISetReady(bool can_use)
        {
            if (ready == can_use)
                return;
            ready = can_use;
            Refresh();
        }

        // The player's leader card was lifted out of its slot, or put back
        public void UISetLifted(bool is_lifted)
        {
            if (lifted == is_lifted)
                return;
            lifted = is_lifted;
            Refresh();
        }

        // The crown: gold while the ability can still be used this round; glowing (orange and gold), swelling softly and
        // a button while the leader is lifted; grey once used or after passing; faded out when passive; red when cancelled
        private void Refresh()
        {
            Button button = ActivateButton;
            Image face = button.targetGraphic as Image;
            if (face == null)
                return;
            if (plainFace == null)
                plainFace = face.sprite;
            bool passive = type == Row.LeaderPassive;
            bool usable = ready && !used && !cancelled && !passive;
            bool glowing = usable && lifted && IsPlayers && UITheme.Current.leaderReady != null;
            face.sprite = glowing ? UITheme.Current.leaderReady : plainFace;
            face.color = cancelled ? CancelledRed : used ? UsedGrey : passive ? PassiveFaded : usable ? Color.white : UsedGrey;
            if (button.transition == Selectable.Transition.None)
                face.canvasRenderer.SetColor(Color.white); // no tint left over from the button's earlier look
            button.interactable = glowing;
            ColorBlock colors = button.colors;
            colors.disabledColor = Color.white; // the face carries the state
            button.colors = colors;

            if (glowing && !throb.IsActive())
                throb = UIMotion.Throb(button.transform);
            else if (!glowing && throb.IsActive())
            {
                throb.TryCancel();
                button.transform.localScale = Vector3.one;
            }
        }

        // The ability's state in a few words, for the player sheet (PlayerSheet): and whether it is good news for its side
        public string StateText(out bool usable)
        {
            usable = false;
            if (cancelled)
                return "Cancelled by the other leader";
            if (type == Row.LeaderPassive)
                return "Passive: always in effect";
            if (used)
                return "Used";
            usable = ready;
            return ready ? (IsPlayers ? "Ready to use" : "Not used yet") : "Not used yet, not this round";
        }

        // The crown's picture (gold), for the player sheet
        public Sprite CrownSprite
        {
            get
            {
                Image face = ActivateButton.targetGraphic as Image;
                return plainFace != null ? plainFace : face != null ? face.sprite : null;
            }
        }

        private string TooltipText()
        {
            string whose = IsPlayers ? "Leader ability" : "Opponent's leader ability";
            if (cancelled)
                return whose + ": cancelled by the other leader";
            if (type == Row.LeaderPassive)
                return whose + ": passive, always in effect";
            if (used)
                return whose + ": used";
            if (!IsPlayers)
                return whose + ": not used yet";
            string press = GameInput.IsTouch ? "tap" : "click";
            return lifted ? "Leader ability: " + press + " to use it"
                : "Leader ability: " + press + " your leader card to lift it, then " + press + " this crown to use it";
        }

        // Reference: the crown's button (Activate). It uses the lifted leader; the controller marks the crown used once the
        // ability really happened.
        public void ActivateLeader()
        {
            if (!IsPlayers)
                return;
            if (controller.battleState == BattleState.PlayerTurn)
                controller.LeaderCrownClicked();
        }
    }
}
