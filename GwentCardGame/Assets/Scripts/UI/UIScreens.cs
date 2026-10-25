using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Gwent.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace Gwent.UI
{
    // The menus in UI Toolkit: a UXML document (Assets/UI/Toolkit, styled by Gwent.uss) on a Panel Renderer of its own.
    // Two panels, both copies of UITheme's Panel Settings (the 1920x1080 reference of the canvases, matched on the height
    // and scaled by the "menu size" setting); panels and overlay canvases sort together:
    // - screens (the main menu), at 400: above a scene's canvases (0), under the uGUI layer (UIOverlay, 500);
    // - dialogs, at 600: above everything a scene shows, the battle's overlays included. A dialog is one of UIOverlay's
    //   modals: Escape and right click close it, and the scene's own keys wait.
    public static class UIScreens
    {
        public enum Layer { Screen, Dialog }

        private const int ScreenOrder = 400;
        private const int DialogOrder = 600;
        private static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

        private static readonly PanelSettings[] panels = new PanelSettings[2];
        private static readonly List<VisualElement> roots = new List<VisualElement>(); // the documents shown, for Find

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            GameSettings.Changed -= ApplyMenuScale;
            Array.Clear(panels, 0, panels.Length);
            roots.Clear();
        }

        // Shows the document; bind finds its elements and hooks them up, each time the document is rebuilt
        public static PanelRenderer Show(string name, VisualTreeAsset document, Action<VisualElement> bind, Layer layer = Layer.Screen)
        {
            GameObject screen = new GameObject(name);
            PanelRenderer renderer = screen.AddComponent<PanelRenderer>();
            int bound = -1;
            renderer.RegisterUIReloadCallback((_, root, version) =>
            {
                if (version == bound)
                    return; // the same document as already bound
                bound = version;
                // A Panel Renderer's root does not fill the panel by itself, and lets clicks through where the document
                // shows nothing (a dialog's dimmer takes them)
                root.pickingMode = PickingMode.Ignore;
                root.style.position = Position.Absolute;
                root.style.left = 0;
                root.style.top = 0;
                root.style.right = 0;
                root.style.bottom = 0;
                root.EnableInClassList("phone", UIKit.PhoneScale > 1); // a phone's own sizes (Gwent.uss)
                roots.RemoveAll(shown => shown.panel == null); // closed, or gone with their scene
                roots.Add(root);
                bind(root);
            });
            renderer.panelSettings = Panel(layer);
            renderer.visualTreeAsset = document;
            return renderer;
        }

        // A dialog: the document on the dialogs' panel, one of UIOverlay's modals until Close (or Escape, right click;
        // may_close: asked first, false keeps it open)
        public static GameObject ShowDialog(string name, VisualTreeAsset document, Action<VisualElement> bind, Action on_close = null, bool close_on_right_click = true,
            Func<bool> may_close = null)
        {
            GameObject dialog = Show(name, document, bind, Layer.Dialog).gameObject;
            UIOverlay.PushModal(dialog, on_close, close_on_right_click, may_close);
            return dialog;
        }

        // Gives the element the keyboard's focus. Keys reach a panel through the EventSystem only while its handler is the
        // selected object, or a dialog's keys would go on moving the menu behind it; focusing an element selects it only
        // once the panel has its handler, which appears a frame or so after the panel: so it is selected here too.
        public static void Focus(VisualElement element)
        {
            if (element == null || element.panel == null)
                return;
            element.Focus();
            SelectPanel(element).Forget();
        }

        private static async UniTaskVoid SelectPanel(VisualElement element)
        {
            for (int frame = 0; frame < 10 && element.panel != null && EventSystem.current != null; frame++)
            {
                foreach (PanelEventHandler handler in UnityEngine.Object.FindObjectsByType<PanelEventHandler>())
                    if (handler.panel == element.panel)
                    {
                        EventSystem.current.SetSelectedGameObject(handler.gameObject);
                        return;
                    }
                await UniTask.Yield();
            }
        }

        // The dialog an element belongs to closes
        public static void Close(VisualElement element)
        {
            PanelRenderer renderer = PanelRenderer.FindPanelRenderer(element);
            if (renderer != null)
                UIOverlay.CloseModal(renderer.gameObject);
        }

        // The element with the keyboard's focus in the screens and dialogs shown, or null
        public static VisualElement Focused()
        {
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                if (roots[i].panel != null && roots[i].panel.focusController.focusedElement is VisualElement focused)
                    return focused;
            }
            return null;
        }

        // An element of a document on show, by name (the newest document first), or null: tools and tests reach the
        // screens through it
        public static VisualElement Find(string name)
        {
            roots.RemoveAll(root => root.panel == null); // closed, or gone with their scene
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                VisualElement element = roots[i].Q(name);
                if (element != null)
                    return element;
            }
            return null;
        }

        // A copy of the theme's Panel Settings per layer: its own sort order, and the menu size changes its reference
        // resolution
        private static PanelSettings Panel(Layer layer)
        {
            PanelSettings panel = panels[(int)layer];
            if (panel == null)
            {
                if (Array.TrueForAll(panels, p => p == null))
                    GameSettings.Changed += ApplyMenuScale;
                panel = UnityEngine.Object.Instantiate(UITheme.Current.panelSettings);
                panel.name = layer == Layer.Dialog ? "Dialogs" : "Screens";
                panel.sortingOrder = layer == Layer.Dialog ? DialogOrder : ScreenOrder;
                panels[(int)layer] = panel;
                ApplyMenuScale();
            }
            return panel;
        }

        private static void ApplyMenuScale()
        {
            foreach (PanelSettings panel in panels)
                if (panel != null)
                    panel.referenceResolution = Vector2Int.RoundToInt(ReferenceResolution / UIKit.MenuScale);
        }
    }
}
