using System.Collections.Generic;
using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Gwent.Battle
{
    // A player's panel on the battle board: portrait, name, faction, hand count, life gems and total score
    public class SidePanel : MonoBehaviour, IPointerClickHandler
    {
        public Sprite redRuby;
        public Sprite greyRuby;
        public List<Sprite> factionShields; // Monsters, Nilfgaard, Northern Realms, Scoia'tael, Skellige

        private Image factionShield;
        private Faction faction;
        private bool isPlayers;
        private TextMeshProUGUI nameText;
        private TextMeshProUGUI deckText;
        private TextMeshProUGUI totalText;
        private TextMeshProUGUI handCountText;
        private Image rubyLeft;
        private Image rubyRight;
        private Image portrait;
        private Image portraitCircle; // the round, masked picture
        private Image border;         // the silver ring around it
        private Image awayVeil;
        private Image[] edges;        // the band's top and bottom lines
        private Image[] edgeGlows;    // their glow out of the band
        private Sprite edgeSprite;
        private Color edgeColor;
        private float edgeHeight;
        private int shownTurnState = -1;

        // The Witcher 3's tones: a thin amber line, a soft orange glow
        private const float TurnEdgeHeight = 2;
        private const float TurnGlowHeight = 14;
        private static readonly Color TurnEdgeGold = new Color(0.98f, 0.74f, 0.4f);
        private static readonly Color TurnGlowGold = new Color(1f, 0.52f, 0.12f, 0.6f);
        private static readonly Color AwayGrey = new Color(0.55f, 0.55f, 0.55f);
        private const float NameClearOfScore = 34f; // the name's box reaches 26 into the score's badge, its laurel 8 more

        // Looked up once instead of on every board refresh
        private void CacheReferences()
        {
            if (totalText != null)
                return;
            Transform avatar_t = transform.Find("Avatar");
            factionShield = avatar_t.Find("faction").GetComponent<Image>();
            Transform portrait_t = avatar_t.Find("Image").Find("Portrait");
            portrait = portrait_t != null ? portrait_t.GetComponent<Image>() : null;
            portraitCircle = avatar_t.Find("Image").GetComponent<Image>();
            border = avatar_t.Find("Border").GetComponent<Image>();
            nameText = transform.Find("TextInfo").Find("Name").GetComponent<TextMeshProUGUI>();
            // A long name stops short of the score's laurel: smaller, down to 16, then cut short with "…"
            nameText.margin = new Vector4(0, 0, NameClearOfScore, 0);
            nameText.fontSizeMax = nameText.fontSize;
            nameText.fontSizeMin = 16;
            nameText.enableAutoSizing = true;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            deckText = transform.Find("TextInfo").Find("Deck").GetComponent<TextMeshProUGUI>();
            totalText = transform.Find("Total").Find("Number").GetComponent<TextMeshProUGUI>();
            handCountText = transform.Find("HandCount").Find("Number").GetComponent<TextMeshProUGUI>();
            rubyLeft = transform.Find("RubyL").GetComponent<Image>();
            rubyRight = transform.Find("RubyR").GetComponent<Image>();
        }

        // The band's background, the whole band's place (the player sheet opens beside it)
        public RectTransform Band
        {
            get { return (RectTransform)transform.Find("Cover"); }
        }

        // A tap or a click anywhere on the band (its background, name, faction, gems, score; in a LAN battle your portrait
        // is the quick chat's): this side's sheet, everything about it at a glance
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                PlayerSheet.Open(isPlayers);
        }

        // The round picture (what a LAN battle's chat bubbles come out of; a click on yours opens the talk: ChatView)
        public RectTransform Picture
        {
            get
            {
                CacheReferences();
                return portraitCircle.rectTransform;
            }
        }

        // The side's faction: its shield and its name, which tell its ability when the pointer rests on them (or a finger
        // is held on them), as the crown tells the leader's
        public void UISetGeneral(Faction faction, bool is_player)
        {
            CacheReferences();
            if (factionShield.GetComponent<UITooltip>() == null)
                foreach (Graphic part in new Graphic[] { factionShield, deckText })
                {
                    part.raycastTarget = true;
                    part.gameObject.AddComponent<UITooltip>().text = AbilityTooltip;
                }
            this.faction = faction;
            isPlayers = is_player;
            string faction_name = faction.Code();
            switch (faction)
            {
                case Faction.Monsters:
                    faction_name = "Monsters";
                    factionShield.sprite = factionShields[0];
                    break;
                case Faction.Nilfgaard:
                    faction_name = "Nilfgaard";
                    factionShield.sprite = factionShields[1];
                    break;
                case Faction.NorthernRealms:
                    faction_name = "Northern Realms";
                    factionShield.sprite = factionShields[2];
                    break;
                case Faction.Scoiatael:
                    faction_name = "Scoia'tael";
                    factionShield.sprite = factionShields[3];
                    break;
                case Faction.Skellige:
                    faction_name = "Skellige";
                    factionShield.sprite = factionShields[4];
                    break;
            }
            deckText.text = faction_name;
        }

        private string AbilityTooltip()
        {
            string ability = faction.Ability();
            if (ability == null)
                return null;
            return (isPlayers ? "Faction ability" : "Opponent's faction ability") + " (" + deckText.text + "): " + ability;
        }

        // Player name, and the leader card's art in the round portrait frame (Avatar/Image masks Avatar/Image/Portrait)
        public void UISetPlayer(string player_name, int leader)
        {
            CacheReferences();
            nameText.text = player_name;
            if (portrait == null)
                return;
            CardArt.ShowPortrait(portrait, leader); // the leader's face in the circle
            portrait.enabled = true;
        }

        public void UIUpdateTotalScore(List<int> close_list, List<int> range_list, List<int> siege_list)
        {
            CacheReferences();
            int total = 0;
            foreach (int number in close_list)
                total += number;
            foreach (int number in range_list)
                total += number;
            foreach (int number in siege_list)
                total += number;
            totalText.text = total.ToString();
        }

        public void UIUpdateHandCount(int cards_number)
        {
            CacheReferences();
            handCountText.text = cards_number.ToString();
        }

        // Whose turn it is shows as in The Witcher 3: that side's band is edged with glowing gold lines, top and bottom,
        // all along. A side whose player is away (a LAN battle's other device, its link lost) is greyed out until it is
        // back. (A side that has passed says so by its score: BoardView's "Passed".)
        public void UISetTurnState(bool active, bool away)
        {
            CacheReferences();
            if (awayVeil == null)
                CreateTurnMarkers();
            int state = away ? 2 : active ? 1 : 0;
            if (state == shownTurnState)
                return;
            shownTurnState = state;
            for (int i = 0; i < edges.Length; i++)
            {
                Image edge = edges[i];
                edge.sprite = state == 1 ? null : edgeSprite; // even all along, not fading out to the right
                edge.color = state == 1 ? TurnEdgeGold : edgeColor;
                edge.rectTransform.sizeDelta = new Vector2(edge.rectTransform.sizeDelta.x, state == 1 ? TurnEdgeHeight : edgeHeight);
                edgeGlows[i].enabled = state == 1;
            }
            awayVeil.enabled = state == 2;
            Color picture = state == 2 ? AwayGrey : Color.white;
            border.color = picture;
            portraitCircle.color = picture;
            if (portrait != null)
                portrait.color = picture;
        }

        private void CreateTurnMarkers()
        {
            Transform cover = transform.Find("Cover");
            edges = new[] { cover.Find("EdgeTop").GetComponent<Image>(), cover.Find("EdgeBottom").GetComponent<Image>() };
            edgeSprite = edges[0].sprite;
            edgeColor = edges[0].color;
            edgeHeight = edges[0].rectTransform.sizeDelta.y;
            // The glow spreads out of the band from each line, brightest at the line, under the lines (GradientV is
            // opaque at its top: the bottom glow hangs from its line, the top one is turned over to rise from its line)
            edgeGlows = new Image[edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                Image glow = UIKit.NewImage(cover, "EdgeGlow", UITheme.Current.gradientV, TurnGlowGold);
                RectTransform rect = glow.rectTransform;
                float edge_y = i == 0 ? 1 : 0;
                rect.anchorMin = new Vector2(0, edge_y);
                rect.anchorMax = new Vector2(1, edge_y);
                rect.pivot = new Vector2(0.5f, 1);
                rect.sizeDelta = new Vector2(0, TurnGlowHeight);
                rect.anchoredPosition = Vector2.zero;
                if (i == 0)
                    rect.localEulerAngles = new Vector3(0, 0, 180);
                glow.transform.SetAsFirstSibling();
                glow.enabled = false;
                edgeGlows[i] = glow;
            }

            // A grey veil over the picture of a side whose player is away (inside its round mask)
            RectTransform circle = portraitCircle.rectTransform;
            awayVeil = UIKit.NewImage(circle, "AwayVeil", null, new Color(0.28f, 0.28f, 0.3f, 0.55f));
            awayVeil.rectTransform.Stretch();
            awayVeil.transform.SetAsLastSibling();
            awayVeil.enabled = false;
        }

        // The gem a side loses when it is down to this many lives (the left one goes first)
        public Image LostGem(int lives)
        {
            CacheReferences();
            return lives >= 1 ? rubyLeft : rubyRight;
        }

        // Red gems for the lives left, grey for the lost ones
        public void UIUpdateGemLives(int lives)
        {
            CacheReferences();
            if (lives < 0 || lives > 2)
            {
                Debug.LogError("UIUpdateGemLives: Unexpected value: " + lives);
                return;
            }
            rubyLeft.sprite = lives >= 2 ? redRuby : greyRuby;
            rubyRight.sprite = lives >= 1 ? redRuby : greyRuby;
        }
    }
}
