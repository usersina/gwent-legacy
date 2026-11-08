using Gwent.Core;
using Gwent.Rules;
using Gwent.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Gwent.DeckBuilding
{
    // The deck builder's header: the factions' banners in a row, the chosen one in the middle and the others smaller, fainter
    // and turned away the further they are. A click on a neighbour (on the buttons laid over them), on a dot below, or a swipe
    // brings another banner to the middle; the change is announced once it has nearly arrived. The banners (each faction's
    // shield and its name, written live) and their dots are made here, one for each of CardGrid.HeaderFactions.
    public class FactionHeader : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const float Spacing = 1.1f;           // from one banner's centre to the next, in banner widths
        private const float SnapSpeed = 10f;          // the glide closes this share of the distance per second (game time)
        // This far from the middle (in the canvas's 1920x1080 units, so at any window size) a banner is almost transparent...
        private const float FadeDistance = 1000f;
        private const float TurnDistance = 1700f;     // ... and this far, half its size and turned 60 degrees away
        private const float SwipeCarry = 0.15f;       // a released swipe carries on for this long (at its speed) before it snaps

        public RectTransform content;
        public Button previousButton;
        public Button nextButton;
        public Transform pagination;
        public int startingPanel;                     // set before the header's Start: the banner it opens on
        public UnityEvent onPanelChanged = new UnityEvent();

        public int CurrentPanel { get; private set; }
        public int TargetPanel { get; private set; }

        private const float BannerWidth = 446, BannerHeight = 112, ShieldWidth = 100;
        private const float DotSpacing = 40;

        private RectTransform[] panels;
        private CanvasGroup[] groups;
        private Toggle[] dots;
        private Canvas canvas;
        private float step;
        private bool dragging;
        private float dragSpeed;

        private void Awake()
        {
            canvas = GetComponentInParent<Canvas>();
            BuildBanners(CardGrid.HeaderFactions);
            panels = new RectTransform[content.childCount];
            groups = new CanvasGroup[panels.Length];
            for (int i = 0; i < panels.Length; i++)
            {
                panels[i] = (RectTransform)content.GetChild(i);
                groups[i] = panels[i].GetComponent<CanvasGroup>();
            }
            step = panels.Length > 0 ? panels[0].rect.width * Spacing : 1f;
            for (int i = 0; i < panels.Length; i++)
                panels[i].anchoredPosition = new Vector2(i * step, 0f);

            dots = pagination != null ? pagination.GetComponentsInChildren<Toggle>(true) : new Toggle[0];
            for (int i = 0; i < dots.Length; i++)
            {
                int panel = i;
                dots[i].onValueChanged.AddListener(on =>
                {
                    if (on)
                        GoToPanel(panel);
                    else if (panel == TargetPanel) // the current dot stays lit
                        dots[panel].SetIsOnWithoutNotify(true);
                });
            }
            if (previousButton != null)
                previousButton.onClick.AddListener(GoToPreviousPanel);
            if (nextButton != null)
                nextButton.onClick.AddListener(GoToNextPanel);
        }

        private void Start()
        {
            CurrentPanel = TargetPanel = Mathf.Clamp(startingPanel, 0, panels.Length - 1);
            content.anchoredPosition = new Vector2(PositionOf(TargetPanel), content.anchoredPosition.y);
            LightDots();
            ShapePanels();
        }

        // A banner for each faction: its shield (the battle's side panel shows it too) and its name in the menus' heading
        // lettering, a long name made smaller to fit; and a dot below for each, the first dot's copies, centred
        private void BuildBanners(Faction[] factions)
        {
            foreach (Faction faction in factions)
            {
                RectTransform banner = UIKit.Rect(faction.Code(), content);
                banner.sizeDelta = new Vector2(BannerWidth, BannerHeight);
                banner.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
                Image shield = UIKit.NewImage(banner, "Shield", SpriteCache.Load("Stats/faction/" + faction.Code()), Color.white);
                shield.preserveAspect = true;
                RectTransform shield_rect = shield.rectTransform;
                shield_rect.anchorMin = shield_rect.anchorMax = shield_rect.pivot = new Vector2(0, 0.5f);
                shield_rect.anchoredPosition = Vector2.zero;
                shield_rect.sizeDelta = new Vector2(ShieldWidth, BannerHeight);
                TextMeshProUGUI name = UIKit.Text(banner, faction.LocalName(), UIKit.TextStyle.Heading, TextAlignmentOptions.Center);
                name.rectTransform.Stretch(ShieldWidth + 8, 0, 0, 0);
                name.enableAutoSizing = true;
                name.fontSizeMin = 26;
                name.fontSizeMax = name.fontSize;
            }

            if (pagination == null || pagination.childCount == 0)
                return;
            Transform first = pagination.GetChild(0);
            while (pagination.childCount < factions.Length)
                Instantiate(first, pagination, false).name = (pagination.childCount).ToString();
            RectTransform dot = (RectTransform)first;
            float span = (factions.Length - 1) * DotSpacing + dot.rect.width;
            ((RectTransform)pagination).sizeDelta = new Vector2(span, ((RectTransform)pagination).sizeDelta.y);
            for (int i = 0; i < pagination.childCount; i++)
            {
                RectTransform each = (RectTransform)pagination.GetChild(i);
                each.anchorMin = each.anchorMax = new Vector2(0, 0.5f);
                each.anchoredPosition = new Vector2(i * DotSpacing + each.pivot.x * each.rect.width, 0);
            }
        }

        public void GoToPanel(int panel)
        {
            if (panel < 0 || panel >= panels.Length)
                return;
            TargetPanel = panel;
            LightDots();
        }

        public void GoToPreviousPanel() { GoToPanel(TargetPanel - 1); }

        public void GoToNextPanel() { GoToPanel(TargetPanel + 1); }

        private void Update()
        {
            if (!dragging)
            {
                float x = content.anchoredPosition.x, target = PositionOf(TargetPanel);
                x = Mathf.Lerp(x, target, Time.deltaTime * SnapSpeed);
                content.anchoredPosition = new Vector2(x, content.anchoredPosition.y);
                if (CurrentPanel != TargetPanel && Mathf.Abs(x - target) < ((RectTransform)transform).rect.width / 10f)
                {
                    CurrentPanel = TargetPanel;
                    onPanelChanged.Invoke();
                }
            }
            ShapePanels();
        }

        //----------------------------------------Swipes----------------------------------------//
        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = true;
            dragSpeed = 0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float dx = eventData.delta.x / (canvas != null ? canvas.scaleFactor : 1f);
            float x = content.anchoredPosition.x;
            // Past the first or last banner the row only follows halfway, as a hint that there is nothing more
            if (x + dx > PositionOf(0) || x + dx < PositionOf(panels.Length - 1))
                dx *= 0.5f;
            content.anchoredPosition = new Vector2(x + dx, content.anchoredPosition.y);
            if (Time.unscaledDeltaTime > 0f)
                dragSpeed = Mathf.Lerp(dragSpeed, dx / Time.unscaledDeltaTime, 0.5f);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
            float carried = content.anchoredPosition.x + dragSpeed * SwipeCarry;
            GoToPanel(Mathf.Clamp(Mathf.RoundToInt(-carried / step), 0, panels.Length - 1));
        }

        //----------------------------------------Looks----------------------------------------//
        private float PositionOf(int panel) { return -panel * step; }

        private void LightDots()
        {
            for (int i = 0; i < dots.Length; i++)
                dots[i].SetIsOnWithoutNotify(i == TargetPanel);
        }

        // Each banner by its distance from the middle: smaller, fainter and turned away (eased, flat at both ends)
        private void ShapePanels()
        {
            for (int i = 0; i < panels.Length; i++)
            {
                float from_middle = panels[i].anchoredPosition.x + content.anchoredPosition.x;
                float far = Mathf.Abs(from_middle);
                float scale = Mathf.Lerp(1f, 0.5f, Ease(far / TurnDistance));
                panels[i].localScale = new Vector3(scale, scale, 1f);
                panels[i].localRotation = Quaternion.Euler(0f, Mathf.Lerp(60f, -60f, Ease((from_middle + TurnDistance) / (2f * TurnDistance))), 0f);
                if (groups[i] != null)
                    groups[i].alpha = Mathf.Lerp(1f, 0.05f, Ease(far / FadeDistance));
            }
        }

        private static float Ease(float t) { return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)); }
    }
}
