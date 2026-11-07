using Gwent.Core;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // Scoia'tael's perk: the player decides who takes the first turn. A themed dialog over the dimmed board, built
    // the first time the picker shows.
    public class FirstTurnPicker : MonoBehaviour
    {
        [HideInInspector]
        public BattleController controller;

        private GameObject dialog;
        private Button goFirst;

        private void Start()
        {
            controller = BattleController.Instance;
        }

        private void OnEnable()
        {
            if (dialog == null)
                Build();
            UIKit.Focus(goFirst); // Enter goes first
        }

        private void Build()
        {
            RectTransform panel = UIKit.Panel(transform, "Dialog").Place(new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(900, 420));
            UIKit.Ornament(panel, new Vector2(0.5f, 1f), new Vector2(0, -46), 400);

            TextMeshProUGUI title = UIKit.Text(panel, Loc.T(Loc.UI, "first.title"), UIKit.TextStyle.Heading, TextAlignmentOptions.Center);
            title.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0, -100), new Vector2(820, 60));
            TextMeshProUGUI body = UIKit.Text(panel, Loc.T(Loc.UI, "first.body"), UIKit.TextStyle.Body, TextAlignmentOptions.Center);
            body.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0, -168), new Vector2(820, 44));
            TextMeshProUGUI hint = UIKit.Text(panel, Loc.T(Loc.UI, "first.hint"), UIKit.TextStyle.Caption, TextAlignmentOptions.Center);
            hint.rectTransform.Place(new Vector2(0.5f, 1f), new Vector2(0, -214), new Vector2(820, 36));

            goFirst = UIKit.Button(panel, Loc.T(Loc.UI, "first.go_first"), UIKit.ButtonStyle.Primary, GoFirst, new Vector2(360, 72));
            ((RectTransform)goFirst.transform).Place(new Vector2(0.5f, 0f), new Vector2(-195, 78), new Vector2(360, 72));
            Button goSecond = UIKit.Button(panel, Loc.T(Loc.UI, "first.go_second"), UIKit.ButtonStyle.Secondary, GoSecond, new Vector2(360, 72));
            ((RectTransform)goSecond.transform).Place(new Vector2(0.5f, 0f), new Vector2(195, 78), new Vector2(360, 72));
            dialog = panel.gameObject;
        }

        public void GoFirst()
        {
            if (controller == null)
                controller = BattleController.Instance;
            GameLog.Event(LogCategory.Battle, "first_turn_chosen", "first", "player");
            controller.ChooseFirstPlayer(true);
            Close();
        }

        public void GoSecond()
        {
            if (controller == null)
                controller = BattleController.Instance;
            GameLog.Event(LogCategory.Battle, "first_turn_chosen", "first", "enemy");
            controller.ChooseFirstPlayer(false);
            Close();
        }

        // The dialog's buttons give up the keyboard focus (it would swallow Enter in the redraw that follows)
        private void Close()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            gameObject.SetActive(false);
        }
    }
}
