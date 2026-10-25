package com.usersina.gwentlegacy.update;

import android.app.Activity;
import android.app.PendingIntent;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageInstaller;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;

// The game's update on Android (Gwent.Updates.AndroidUpdate): the downloaded, checked APK handed to Android's package
// installer in a session. Android then asks the player to confirm (and, the first time, to allow this app to install
// apps); the answer comes back to a receiver registered here, which opens that confirmation, then keeps the outcome for
// the game to read (status, message). A successful update replaces the running game.
public final class GwentUpdateInstaller {
    public static final int NONE = Integer.MIN_VALUE;     // no install started
    public static final int WAITING = -1000;              // started, no answer from Android yet
    private static final String ACTION = "com.usersina.gwentlegacy.UPDATE_STATUS";

    private static volatile int status = NONE;
    private static volatile String message = "";
    private static BroadcastReceiver receiver;

    private GwentUpdateInstaller() {
    }

    // The last install's state: NONE, WAITING, or Android's PackageInstaller.STATUS_* (-1: waiting for the player,
    // 0: installed, 3: cancelled, others: failed)
    public static int status() {
        return status;
    }

    public static String message() {
        return message;
    }

    // Whether Android lets this app install apps (the player allows it once, in Android's settings)
    public static boolean canInstall(Context context) {
        return Build.VERSION.SDK_INT < 26 || context.getPackageManager().canRequestPackageInstalls();
    }

    // Android's page where the player allows this app to install apps
    public static void openInstallSettings(Activity activity) {
        Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + activity.getPackageName()));
        activity.startActivity(intent);
    }

    // Starts installing the APK at that path: "" once handed over, or what went wrong
    public static String install(Activity activity, String path) {
        try {
            listen(activity.getApplicationContext());
            status = WAITING;
            message = "";
            File apk = new File(path);
            PackageInstaller installer = activity.getPackageManager().getPackageInstaller();
            PackageInstaller.SessionParams params = new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
            params.setAppPackageName(activity.getPackageName());
            params.setSize(apk.length());
            int id = installer.createSession(params);
            PackageInstaller.Session session = installer.openSession(id);
            try (InputStream in = new FileInputStream(apk); OutputStream out = session.openWrite("update.apk", 0, apk.length())) {
                byte[] buffer = new byte[1 << 16];
                int read;
                while ((read = in.read(buffer)) > 0)
                    out.write(buffer, 0, read);
                session.fsync(out);
            }
            // Android fills the outcome in: a mutable intent, explicit (this app's own)
            Intent intent = new Intent(ACTION).setPackage(activity.getPackageName());
            int flags = PendingIntent.FLAG_UPDATE_CURRENT | (Build.VERSION.SDK_INT >= 31 ? PendingIntent.FLAG_MUTABLE : 0);
            PendingIntent pending = PendingIntent.getBroadcast(activity, id, intent, flags);
            session.commit(pending.getIntentSender());
            session.close();
            return "";
        } catch (Exception e) {
            status = PackageInstaller.STATUS_FAILURE;
            message = e.toString();
            return message;
        }
    }

    // The outcomes, to this app only
    private static synchronized void listen(Context context) {
        if (receiver != null)
            return;
        receiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context c, Intent intent) {
                int got = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE);
                String text = intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE);
                message = text != null ? text : "";
                status = got;
                if (got == PackageInstaller.STATUS_PENDING_USER_ACTION) {
                    Intent confirm = intent.getParcelableExtra(Intent.EXTRA_INTENT);
                    if (confirm != null) {
                        confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                        c.startActivity(confirm);
                    }
                }
            }
        };
        IntentFilter filter = new IntentFilter(ACTION);
        if (Build.VERSION.SDK_INT >= 33)
            context.registerReceiver(receiver, filter, Context.RECEIVER_NOT_EXPORTED);
        else
            context.registerReceiver(receiver, filter);
    }
}
