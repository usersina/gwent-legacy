using System.Collections;
using System.Collections.Generic;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gwent.Battle
{
    public class RowClick : MonoBehaviour, IPointerClickHandler
    {
        [HideInInspector]
        public BattleController controller;

        void Start()
        {
            controller = BattleController.Instance;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) // Detect a left click
            {
                OnRowClick();
            }
        }

        public void OnRowClick()
        {
            // Checking for the turn is un-needed, already checked when highlighting the field
            // This is the parent of the parent of the gameobject of this script
            // Basically the field gameObject (either PlayerField or EnemyField)
            if (controller == null)
                controller = BattleController.Instance;
            GameObject parent_go = transform.parent.parent.gameObject;
            List<GameObject> highLighted_fields = parent_go.GetComponent<RowPicker>().highLightedRows;

            if (controller.battleState == BattleState.PlayerTurn) // Just to be extra sure
            {
                // The lifted leader: its crown uses it, not the board
                if (controller.LeaderSelected)
                {
                    controller.HintLeaderCrown();
                    return;
                }
                if (controller.IsSelectedCardPlayable())
                {
                    GameObject targetField = GetPlayableTargetField(highLighted_fields);
                    if (targetField != null)
                    {
                        // Just be sure to update the lists
                        controller.DirectlyPlaceCard(controller.selectedCard, targetField, true);
                    }
                    else
                    {
                        // Not a place for this card: say so instead of doing nothing
                        GameAudio.PlaySfx("invalid");
                        Card selected = controller.selectedCard.GetComponent<CardFace>().Card;
                        if (selected.ability == Ability.Decoy)
                            UIOverlay.Toast(Loc.T(Loc.UI, "row.decoy"));
                        else if (selected.row == Row.Weather || selected._id == CardIds.Scorch)
                            UIOverlay.Toast(Loc.T(Loc.UI, "row.weather"));
                    }
                }
            }
        }

        private GameObject GetPlayableTargetField(List<GameObject> highLightedFields)
        {
            Card selectedStats = controller.selectedCard.GetComponent<CardFace>().Card;
            if (selectedStats.faction == Faction.Special)
            {
                // Weather and scorch only go on the weather board
                if (selectedStats.row == Row.Special)
                {
                    if (highLightedFields.Contains(gameObject))
                        return gameObject;

                    Transform specialRow = transform.parent.Find("Special");
                    if (specialRow != null && highLightedFields.Contains(specialRow.gameObject))
                        return specialRow.gameObject;
                }

                return null;
            }

            if (highLightedFields.Contains(gameObject))
                return gameObject;

            return null;
        }
    }
}
