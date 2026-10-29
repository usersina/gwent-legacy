using System;
using System.Collections.Generic;
using Gwent.Core;
using Gwent.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Gwent.Story
{
    // A region's map (UI Toolkit), as The Witcher 3's: dragged around with the mouse or a finger, zoomed with the wheel, a
    // pinch or the keys (+ and -, the arrows move it), never smaller than the screen it covers. The image is a child,
    // scaled and moved; the markers lie in a layer over it, placed where the map puts their point, so they keep their size
    // whatever the zoom (BoardZoom's ways for the battle's board: zoom about the fingers' middle, a drag only past a few
    // millimetres, so a tap stays a tap). Markers that would cover each other at this zoom (an inn's players, zoomed out)
    // are pushed apart on the screen; zoomed in, each stands on its point. Nothing moves while a dialog is open.
    public class MapView : VisualElement
    {
        private const float DragMm = 3;         // further than this, a press is a drag (no tap on what it started on)
        private const float MaxScale = 1.6f;    // the image at most this much larger than its pixels
        private const float WheelStep = 1.15f;  // a wheel notch's zoom
        private const float KeyPan = 900;       // the arrows' pan, view units a second
        private const float KeyZoom = 1.8f;     // + and -, zoom a second

        private readonly VisualElement image = new VisualElement { name = "Image", pickingMode = PickingMode.Ignore };
        private readonly VisualElement layer = new VisualElement { name = "Markers", pickingMode = PickingMode.Ignore };
        private readonly List<(VisualElement marker, Vector2 at)> markers = new List<(VisualElement, Vector2)>();
        private readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>(); // the presses down, where they are
        private Vector2 size;           // the image's pixels
        private float scale = 1;        // view units an image pixel
        private Vector2 origin;         // where the image's top left corner is in the view
        private Vector2 pressed;        // where the press started (a drag is told from a tap by it)
        private bool dragging;
        private VisualElement pressedMarker; // the marker a press began on: clicked if the press stays a tap
        private float pressedAt;        // when (unscaled): a finger held as long as a long press was a look, not a tap
        private float pinch;            // the two fingers' distance when the pinch was last applied
        private Vector2 focusAt = new Vector2(0.5f, 0.5f); // what to show once the size is known
        private float focusZoom;

        public MapView()
        {
            AddToClassList("map-view");
            style.overflow = Overflow.Hidden;
            image.AddToClassList("map-view__image");
            image.style.position = Position.Absolute;
            layer.style.position = Position.Absolute;
            layer.style.left = layer.style.top = layer.style.right = layer.style.bottom = 0;
            Add(image);
            Add(layer);
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
            // On the way down to a marker too: a press on one can become a drag of the map, and one that stays a tap
            // clicks it (Release); its tooltip still sees the press (a finger held on it)
            RegisterCallback<PointerDownEvent>(Down, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(Move, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(Up, TrickleDown.TrickleDown);
            RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId, false), TrickleDown.TrickleDown);
            RegisterCallback<WheelEvent>(Wheel);
            schedule.Execute(Keys).Every(0);
        }

        // Whether the press that ended was a drag (its click is no click on a marker)
        public bool Dragged { get { return dragging; } }

        // The map's image (its pixels' size sets the map's)
        public void Show(Sprite sprite)
        {
            image.style.backgroundImage = new StyleBackground(sprite);
            size = sprite != null ? sprite.rect.size : Vector2.zero;
            image.style.width = size.x;
            image.style.height = size.y;
            Fit();
        }

        public void ClearMarkers()
        {
            foreach ((VisualElement marker, Vector2 _) in markers)
                marker.RemoveFromHierarchy();
            markers.Clear();
        }

        // A marker at a point of the map (0 to 1 from its left and its top): centred on it
        public void AddMarker(VisualElement marker, Vector2 at)
        {
            marker.style.position = Position.Absolute;
            // Its button clicks by the keys only: a press is the map's (a button would keep the pointer, and a drag
            // started on it would move nothing)
            if (marker is Button button)
                button.clickable.activators.Clear();
            layer.Add(marker);
            markers.Add((marker, at));
            PlaceMarkers();
        }

        // Shows that point of the map in the middle (as far as the map goes), zoomed in (0: as it is; 1: the most)
        public void Focus(Vector2 at, float zoom = 0)
        {
            focusAt = at;
            focusZoom = zoom;
            if (size == Vector2.zero || float.IsNaN(layout.width) || layout.width <= 0)
                return; // once the map's size is known (Fit)
            if (zoom > 0)
                scale = Mathf.Lerp(MinScale, MaxScale, zoom);
            Vector2 view = layout.size;
            origin = view / 2 - Vector2.Scale(at, size) * scale;
            Apply();
        }

        // The smallest scale: the image covers the view
        private float MinScale
        {
            get { return size.x > 0 && size.y > 0 ? Mathf.Max(layout.width / size.x, layout.height / size.y) : 1; }
        }

        private void Fit()
        {
            if (size == Vector2.zero || float.IsNaN(layout.width) || layout.width <= 0)
                return;
            scale = Mathf.Clamp(scale, MinScale, Mathf.Max(MinScale, MaxScale));
            Focus(focusAt, focusZoom);
            focusZoom = 0;
        }

        // Zoom about a point of the view (it stays over the same point of the map)
        private void ZoomAbout(Vector2 point, float factor)
        {
            float before = scale;
            scale = Mathf.Clamp(scale * factor, MinScale, Mathf.Max(MinScale, MaxScale));
            origin = point - (point - origin) * (scale / before);
            Apply();
        }

        // The image placed (never leaving the view uncovered), the markers over their points
        private void Apply()
        {
            Vector2 view = layout.size;
            Vector2 shown = size * scale;
            origin.x = Mathf.Clamp(origin.x, view.x - shown.x, 0);
            origin.y = Mathf.Clamp(origin.y, view.y - shown.y, 0);
            image.style.left = origin.x;
            image.style.top = origin.y;
            image.style.width = shown.x;
            image.style.height = shown.y;
            PlaceMarkers();
            focusAt = (view / 2 - origin) / scale;
            focusAt = new Vector2(focusAt.x / Mathf.Max(1, size.x), focusAt.y / Mathf.Max(1, size.y));
        }

        private void Place(VisualElement marker, Vector2 at)
        {
            Vector2 point = origin + Vector2.Scale(at, size) * scale;
            Put(marker, point);
        }

        private static void Put(VisualElement marker, Vector2 point)
        {
            marker.style.left = point.x;
            marker.style.top = point.y;
            marker.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
        }

        // Every marker on its point, those nearer than a marker's width pushed apart (a few passes, each pair moved half
        // the overlap each way; two on the same point apart along a fixed slant)
        private readonly List<Vector2> points = new List<Vector2>();

        private void PlaceMarkers()
        {
            points.Clear();
            foreach ((VisualElement _, Vector2 at) in markers)
                points.Add(origin + Vector2.Scale(at, size) * scale);
            float apart = markers.Count > 0 && markers[0].marker.resolvedStyle.width > 0 ? markers[0].marker.resolvedStyle.width * 1.05f : 64;
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                for (int i = 0; i < points.Count; i++)
                    for (int j = i + 1; j < points.Count; j++)
                    {
                        Vector2 gap = points[j] - points[i];
                        float distance = gap.magnitude;
                        if (distance >= apart)
                            continue;
                        Vector2 away = distance > 0.01f ? gap / distance : new Vector2(0.8f, 0.6f);
                        Vector2 push = away * (apart - distance) / 2;
                        points[i] -= push;
                        points[j] += push;
                        moved = true;
                    }
                if (!moved)
                    break;
            }
            for (int i = 0; i < markers.Count; i++)
                Put(markers[i].marker, points[i]);
        }

        // Where a point of the view is on the map (0 to 1)
        public Vector2 MapPoint(Vector2 view_point)
        {
            Vector2 at = (view_point - origin) / scale;
            return new Vector2(at.x / Mathf.Max(1, size.x), at.y / Mathf.Max(1, size.y));
        }

        //----------------------------------------Input----------------------------------------//
        private static bool Free { get { return !UIOverlay.HasModal; } }

        // Those millimetres in the panel's units (the panel is scaled to the screen)
        private float DragThreshold
        {
            get
            {
                float panel_width = panel != null ? panel.visualTree.layout.width : Screen.width;
                return DragMm * GameInput.PixelsPerMm * panel_width / Mathf.Max(1, Screen.width);
            }
        }

        private void Down(PointerDownEvent e)
        {
            if (!Free)
                return;
            Vector2 at = this.WorldToLocal(e.position);
            pointers[e.pointerId] = at;
            if (pointers.Count == 1)
            {
                pressed = at;
                dragging = false;
                pressedAt = Time.unscaledTime;
                pressedMarker = e.button == 0 ? MarkerAt(e.target as VisualElement) : null;
            }
            else if (pointers.Count == 2)
            {
                pinch = Spread();
                Drag(e.pointerId); // two fingers are no tap
            }
        }

        // The marker an element is (or is in), if any
        private VisualElement MarkerAt(VisualElement element)
        {
            for (VisualElement at = element; at != null && at != this; at = at.parent)
                if (at.parent == layer)
                    return at;
            return null;
        }

        // A press become a drag: the map takes the pointer (a marker it started on gets no click)
        private void Drag(int pointer)
        {
            dragging = true;
            foreach (int id in pointers.Keys)
                if (!this.HasPointerCapture(id))
                    this.CapturePointer(id);
        }

        private void Move(PointerMoveEvent e)
        {
            Vector2 last;
            if (!pointers.TryGetValue(e.pointerId, out last))
                return;
            Vector2 at = this.WorldToLocal(e.position);
            pointers[e.pointerId] = at;
            if (!Free)
                return;
            if (pointers.Count >= 2)
            {
                float spread = Spread();
                if (pinch > 1 && spread > 1)
                    ZoomAbout(Middle(), spread / pinch);
                pinch = spread;
                origin += (at - last) / 2; // the fingers' middle moves the map too
                Apply();
                return;
            }
            if (!dragging && (at - pressed).magnitude > DragThreshold)
                Drag(e.pointerId);
            if (dragging)
            {
                origin += at - last;
                Apply();
            }
        }

        private void Up(PointerUpEvent e)
        {
            Release(e.pointerId, true);
        }

        private void Release(int pointer, bool lifted)
        {
            if (!pointers.Remove(pointer))
                return;
            if (this.HasPointerCapture(pointer))
                this.ReleasePointer(pointer);
            if (pointers.Count == 1)
                foreach (KeyValuePair<int, Vector2> left in pointers)
                    pressed = left.Value; // the other finger goes on dragging from where it is
            // A tap on a marker clicks it, as its key would (a drag's release is no click, nor a finger held to read its
            // tooltip; Dragged stays until the next press)
            bool held = GameInput.IsTouch && Time.unscaledTime - pressedAt >= LongPress.Seconds;
            VisualElement tapped = lifted && !dragging && !held && pointers.Count == 0 ? pressedMarker : null;
            if (pointers.Count == 0)
                pressedMarker = null;
            if (tapped != null && tapped.enabledInHierarchy)
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = tapped;
                    tapped.SendEvent(submit);
                }
        }

        private float Spread()
        {
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int n = 0;
            foreach (Vector2 at in pointers.Values)
            {
                if (n == 0) a = at; else if (n == 1) b = at;
                n++;
            }
            return n >= 2 ? (a - b).magnitude : 0;
        }

        private Vector2 Middle()
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2 at in pointers.Values)
                sum += at;
            return pointers.Count > 0 ? sum / pointers.Count : layout.size / 2;
        }

        private void Wheel(WheelEvent e)
        {
            if (!Free)
                return;
            ZoomAbout(this.WorldToLocal(e.mousePosition), e.delta.y < 0 ? WheelStep : 1 / WheelStep);
            e.StopPropagation();
        }

        // The keys: the arrows move the map, + and - zoom it, about the middle (the letters are the map's tools')
        private void Keys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !Free || size == Vector2.zero)
                return;
            float dt = Time.unscaledDeltaTime;
            Vector2 move = Vector2.zero;
            if (keyboard.leftArrowKey.isPressed) move.x += 1;
            if (keyboard.rightArrowKey.isPressed) move.x -= 1;
            if (keyboard.upArrowKey.isPressed) move.y += 1;
            if (keyboard.downArrowKey.isPressed) move.y -= 1;
            if (move != Vector2.zero)
            {
                origin += move * KeyPan * dt;
                Apply();
            }
            bool zoom_in = keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed;
            bool zoom_out = keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed;
            if (zoom_in != zoom_out)
                ZoomAbout(layout.size / 2, Mathf.Pow(KeyZoom, zoom_in ? dt : -dt));
        }
    }
}
