using System;
using Gwent.Core;
using Gwent.UI;
using Gwent.Updates;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gwent.Menus
{
    // A newer build is out (the main menu's notice opens this): its build, date and download size, what's new (the
    // release's notes), and Update, which does what this platform can (UpdateInstaller: reload the page, install the APK,
    // or replace the game's files and restart it). While it downloads, a bar shows how far, and Later becomes Cancel.
    // Closing it (×, Escape, Back) stops a download. A UI Toolkit dialog: Assets/UI/Toolkit/UpdateDialog.uxml.
    public static class UpdateDialog
    {
        public static bool IsOpen { get; private set; }

        public static void Open(UpdateFeed feed, Action on_close)
        {
            if (feed == null || IsOpen)
                return;
            UpdateInstaller installer = new UpdateInstaller(feed, UpdateFeed.Current);
            IsOpen = true;
            UIScreens.ShowDialog("UpdateDialog", UITheme.Current.updateDialog, root => Bind(root, feed, installer), () =>
            {
                IsOpen = false;
                installer.Cancel();
                on_close?.Invoke();
            });
            GameLog.Event(LogCategory.Ui, "update_dialog", "build", feed.build);
        }

        private static void Bind(VisualElement root, UpdateFeed feed, UpdateInstaller installer)
        {
            UpdateFile file = installer.File;
            root.Q<Label>("Detail").text = "Build " + feed.build + " \u00B7 " + feed.date + (file != null ? " \u00B7 " + Megabytes(file.size) : "")
                + "   (yours: " + BuildInfo.Label + ")";

            ScrollView notes = root.Q<ScrollView>("Notes");
            foreach (UpdateFeed.NoteLine line in UpdateFeed.Lines(feed.notes))
            {
                Label label = new Label(line.Text);
                label.AddToClassList(line.Kind == UpdateFeed.NoteKind.Heading ? "rules__title" : "rules__line");
                if (line.Kind != UpdateFeed.NoteKind.Point && line.Kind != UpdateFeed.NoteKind.SubPoint)
                {
                    label.AddToClassList(line.Kind == UpdateFeed.NoteKind.Heading ? "update__heading" : "update__paragraph");
                    notes.Add(label);
                    continue;
                }
                // A point: a bullet beside its text, the text's lines under one another
                VisualElement point = new VisualElement();
                point.AddToClassList("update__point");
                if (line.Kind == UpdateFeed.NoteKind.SubPoint)
                    point.AddToClassList("update__point--sub");
                label.AddToClassList("update__text");
                Label bullet = new Label("\u2022");
                bullet.AddToClassList("rules__line");
                bullet.AddToClassList("update__bullet");
                point.Add(bullet);
                point.Add(label);
                notes.Add(point);
            }

            Button update = root.Q<Button>("Button Update");
            Button later = root.Q<Button>("Button Later");
            update.clicked += installer.Start;
            later.clicked += () =>
            {
                if (installer.Busy)
                    installer.Cancel();
                else
                    UIScreens.Close(root);
            };
            root.Q<Button>("Button Close").clicked += () => UIScreens.Close(root);

            VisualElement progress = root.Q("Progress");
            VisualElement bar = root.Q("Bar");
            Label status = root.Q<Label>("Status");
            Action refresh = () =>
            {
                installer.Poll();
                bool downloading = installer.State == UpdateInstaller.Stage.Downloading;
                progress.style.visibility = downloading || installer.State == UpdateInstaller.Stage.Checking ? Visibility.Visible : Visibility.Hidden;
                if (file != null && file.size > 0)
                    bar.style.width = Length.Percent(Mathf.Clamp01((float)installer.Received / file.size) * 100f);
                status.text = Status(installer, file);
                update.text = installer.ActionLabel;
                update.SetEnabled(!installer.Busy && installer.State != UpdateInstaller.Stage.Confirming);
                later.text = installer.Busy ? "Cancel" : "Later";
                later.SetEnabled(installer.State != UpdateInstaller.Stage.Restarting);
            };
            refresh();
            root.schedule.Execute(refresh).Every(100);
            // Back from Android's confirmation without an answer: Install again
            Action<bool> focused = has_focus =>
            {
                if (has_focus)
                    installer.Resumed();
            };
            Application.focusChanged += focused;
            root.RegisterCallback<DetachFromPanelEvent>(_ => Application.focusChanged -= focused);
            root.schedule.Execute(() => UIScreens.Focus(update));
        }

        // What the dialog says under the notes, as the update goes
        private static string Status(UpdateInstaller installer, UpdateFile file)
        {
            UpdatePlatform platform = UpdateFeed.Current;
            switch (installer.State)
            {
                case UpdateInstaller.Stage.Downloading:
                    return "Downloading\u2026 " + Megabytes(installer.Received) + (file != null ? " of " + Megabytes(file.size) : "");
                case UpdateInstaller.Stage.Checking:
                    return "Checking the download\u2026";
                case UpdateInstaller.Stage.Unpacking:
                    return "Unpacking\u2026";
                case UpdateInstaller.Stage.Confirming:
                    return "Android asks you to confirm the update; once it's installed, open the game again.";
                case UpdateInstaller.Stage.Restarting:
                    return platform == UpdatePlatform.Web ? "Reloading\u2026" : "Restarting with the new version\u2026";
                case UpdateInstaller.Stage.Failed:
                    return installer.Problem;
            }
            if (installer.Problem != null)
                return installer.Problem;
            if (installer.OfferPage || platform != UpdatePlatform.Web && installer.File == null)
                return "Download it from the release's page.";
            switch (platform)
            {
                case UpdatePlatform.Web: return "The page reloads with the new version.";
                case UpdatePlatform.Android: return "Android asks you to confirm it; then open the game again. Your settings and decks stay.";
                default: return "The game restarts once updated. Your settings and decks stay.";
            }
        }

        private static string Megabytes(long bytes)
        {
            return Mathf.Max(0, Mathf.RoundToInt(bytes / 1048576f)) + " MB";
        }
    }
}
